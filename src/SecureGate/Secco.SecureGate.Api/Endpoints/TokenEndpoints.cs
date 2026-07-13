using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Endpoint de token OIDC (client credentials, ADR-0022). O OpenIddict valida
/// <c>client_id</c>/<c>client_secret</c> e as permissões de scope ANTES do passthrough —
/// aqui apenas montamos a identidade com as claims curtas da ADR-0007.
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
            IOpenIddictApplicationManager applicationManager) =>
        {
            var request = context.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("Requisição OIDC não encontrada no contexto.");

            if (!request.IsClientCredentialsGrantType())
            {
                return Results.Forbid(
                    authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
                    properties: new Microsoft.AspNetCore.Authentication.AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnsupportedGrantType,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Apenas client credentials é suportado nesta fase.",
                    }));
            }

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

            // Scope → audiences (resources): "logstream" → "secco-logstream" (o que os produtos validam)
            var resources = new List<string>();

            await foreach (var resource in scopeManager.ListResourcesAsync(identity.GetScopes(), context.RequestAborted))
            {
                resources.Add(resource);
            }

            identity.SetResources(resources);
            identity.SetDestinations(static _ => [Destinations.AccessToken]);

            return Results.SignIn(new ClaimsPrincipal(identity),
                properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        })
        .AllowAnonymous()               // a autenticação AQUI é o client_secret, validado pelo OpenIddict
        .ExcludeFromDescription();      // endpoint de protocolo: descrito pelo discovery OIDC, não pelo contrato de negócio

        return endpoints;
    }
}
