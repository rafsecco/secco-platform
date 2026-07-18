using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Constants;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Endpoints interativos do OIDC (Fase 6.5, ADR-0022): <c>/connect/authorize</c> (login via
/// cookie do Identity → emissão do authorization code), <c>/connect/userinfo</c> e
/// <c>/connect/logout</c>. Endpoints de protocolo — descritos pelo discovery OIDC, não pelo
/// contrato de negócio (<c>ExcludeFromDescription</c>).
/// </summary>
public static class InteractiveEndpoints
{
    /// <summary>Mapeia os endpoints interativos do OIDC.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapInteractiveEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/connect/authorize", ["GET", "POST"], AuthorizeAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        // Cast a Delegate: UserInfo tem só (HttpContext) e casaria com RequestDelegate (ASP0016)
        endpoints.MapMethods("/connect/userinfo", ["GET", "POST"], (Delegate)UserInfo)
            .AllowAnonymous()
            .ExcludeFromDescription();

        endpoints.MapMethods("/connect/logout", ["GET", "POST"], LogoutAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        UserManager<User> userManager,
        IOpenIddictScopeManager scopeManager)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Requisição OIDC não encontrada no contexto.");

        // Sem cookie de sessão → desafia o esquema do Identity → redireciona para /login
        var authentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);

        if (!authentication.Succeeded)
        {
            return Results.Challenge(
                new AuthenticationProperties
                {
                    // Após o login, o usuário volta exatamente a esta requisição de autorização
                    RedirectUri = context.Request.PathBase + context.Request.Path + context.Request.QueryString,
                },
                [IdentityConstants.ApplicationScheme]);
        }

        var user = await userManager.GetUserAsync(authentication.Principal!);

        if (user is null)
        {
            // Cookie válido mas usuário sumiu (removido) — força novo login
            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = context.Request.PathBase + context.Request.Path + context.Request.QueryString,
                },
                [IdentityConstants.ApplicationScheme]);
        }

        var roles = await userManager.GetRolesAsync(user);

        // ADR-0023: o scope admin só é emitido a operadores de plataforma — login de usuário
        // comum pelo client do AdminPortal NÃO escala para admin, mesmo com o scope permitido.
        // O nome do role é único só por tenant: exigir também o tenant de plataforma impede
        // que um "platform-operator" forjado num tenant de cliente ganhe o scope admin
        // (defesa contra colisão de nome, ADR-0020/0023/0024).
        var scopes = request.GetScopes().AsEnumerable();

        var isPlatformOperator = user.TenantId == SecureGatePlatform.TenantId
            && roles.Contains(SecureGatePlatform.OperatorRole, StringComparer.Ordinal);

        if (!isPlatformOperator)
        {
            scopes = scopes.Where(scope => scope != SecureGateScopes.Admin);
        }

        var granted = scopes.ToList();
        var resources = await OidcPrincipalBuilder.ResolveResourcesAsync(scopeManager, granted, context.RequestAborted);

        // Client first-party confiável (ConsentType Implicit no registro) → sem tela de consent (Fase 6.5)
        var principal = OidcPrincipalBuilder.ForUser(user, roles, granted, resources);

        return Results.SignIn(principal, properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> UserInfo(HttpContext context)
    {
        var principal = (await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;

        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var claims = new Dictionary<string, object?>
        {
            [Claims.Subject] = principal.GetClaim(Claims.Subject),
            [SeccoClaims.TenantId] = principal.GetClaim(SeccoClaims.TenantId),
        };

        if (principal.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = principal.GetClaim(Claims.Email);
        }

        if (principal.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = principal.GetClaim(Claims.Name);
        }

        if (principal.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = principal.GetClaims(SeccoClaims.Role);
        }

        return Results.Json(claims);
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, SignInManager<User> signInManager)
    {
        // Encerra a sessão local (cookie do Identity)...
        await signInManager.SignOutAsync();

        // ...e delega ao OpenIddict a validação do post_logout_redirect_uri e o redirecionamento
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }
}
