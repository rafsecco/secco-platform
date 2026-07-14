using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Secco.AdminPortal.Endpoints;

/// <summary>Endpoints de autenticação do relying party (logout OIDC, Fase 7.1).</summary>
public static class AuthenticationEndpoints
{
    /// <summary>Mapeia o logout: encerra o cookie local e delega o end-session ao SecureGate.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST + antiforgery (o formulário do layout envia o token) — logout não é GET (CSRF)
        endpoints.MapPost("/authentication/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignOutAsync(
                OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = "/" });
        })
        .RequireAuthorization();

        return endpoints;
    }
}
