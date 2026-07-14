using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Infrastructure.Identity;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Endpoint de token OIDC (ADR-0022): client credentials (máquinas, Fase 6.2) e
/// authorization code / refresh token (usuários, Fase 6.5). O OpenIddict valida credenciais,
/// PKCE e o próprio code/refresh ANTES do passthrough — aqui apenas montamos a identidade
/// com as claims curtas da ADR-0007.
/// </summary>
public static class TokenEndpoints
{
    /// <summary>Mapeia o endpoint de token.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/connect/token", async (
            HttpContext context,
            IOpenIddictScopeManager scopeManager,
            IOpenIddictApplicationManager applicationManager,
            UserManager<User> userManager,
            SignInManager<User> signInManager) =>
        {
            var request = context.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("Requisição OIDC não encontrada no contexto.");

            if (request.IsClientCredentialsGrantType())
            {
                return await HandleClientCredentialsAsync(context, request, scopeManager, applicationManager);
            }

            if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
            {
                return await HandleUserGrantAsync(context, scopeManager, userManager, signInManager);
            }

            return UnsupportedGrant("Grant type não suportado.");
        })
        .AllowAnonymous()               // a autenticação AQUI é o client_secret/PKCE, validado pelo OpenIddict
        .ExcludeFromDescription();      // endpoint de protocolo: descrito pelo discovery OIDC, não pelo contrato de negócio

        return endpoints;
    }

    private static async Task<IResult> HandleClientCredentialsAsync(
        HttpContext context,
        OpenIddictRequest request,
        IOpenIddictScopeManager scopeManager,
        IOpenIddictApplicationManager applicationManager)
    {
        // Credenciais e permissões de scope já validadas pelo OpenIddict (client_secret hasheado)
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

        // Claims curtas (ADR-0007): sub = client; sem tenant_id em serviço-a-serviço —
        // o tenant alvo viaja no header X-Tenant-Id (cenário interno, ADR-0005)
        identity.SetClaim(Claims.Subject, request.ClientId);
        identity.SetScopes(request.GetScopes());

        // Roles do client (Fase 6.4, ADR-0021): máquinas usam o MESMO modelo
        // Role + Permission dos usuários — a claim curta 'role' sai no access token
        if (await applicationManager.FindByClientIdAsync(request.ClientId!, context.RequestAborted)
            is Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication { Roles.Length: > 0 } application)
        {
            identity.SetClaims(Claims.Role,
                [.. application.Roles.Split(' ', StringSplitOptions.RemoveEmptyEntries)]);
        }

        identity.SetResources(await OidcPrincipalBuilder.ResolveResourcesAsync(
            scopeManager, identity.GetScopes(), context.RequestAborted));
        identity.SetDestinations(static _ => [Destinations.AccessToken]);

        return Results.SignIn(new ClaimsPrincipal(identity),
            properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> HandleUserGrantAsync(
        HttpContext context,
        IOpenIddictScopeManager scopeManager,
        UserManager<User> userManager,
        SignInManager<User> signInManager)
    {
        // O principal vem do code/refresh que o OpenIddict já validou
        var stored = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;

        var user = stored?.GetClaim(Claims.Subject) is { } subject
            ? await userManager.FindByIdAsync(subject)
            : null;

        // Re-deriva as claims do banco a cada emissão (ADR-0020): usuário desativado/bloqueado
        // ou com role alterado é refletido no refresh — sem esperar o token expirar
        if (user is null || !await signInManager.CanSignInAsync(user))
        {
            return Forbid(Errors.InvalidGrant, "A conta não pode mais ser autenticada.");
        }

        var scopes = stored!.GetScopes();
        var resources = await OidcPrincipalBuilder.ResolveResourcesAsync(scopeManager, scopes, context.RequestAborted);
        var principal = OidcPrincipalBuilder.ForUser(user, await userManager.GetRolesAsync(user), scopes, resources);

        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult UnsupportedGrant(string description) => Forbid(Errors.UnsupportedGrantType, description);

    private static IResult Forbid(string error, string description) =>
        Results.Forbid(
            authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }));
}
