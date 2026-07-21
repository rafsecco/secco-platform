using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Callback pós-autenticação do login federado (ADR-0026): o esquema OIDC do Entra deposita
/// o principal no cookie externo do Identity e redireciona para cá; aqui a decisão fail-closed
/// do <see cref="EntraSignInProcessor"/> transforma (ou não) a identidade provada em sessão.
/// Endpoint de protocolo/UI — fora do contrato de negócio (<c>ExcludeFromDescription</c>).
/// </summary>
public static class FederatedLoginEndpoints
{
    private const string GenericFailureRedirect = "/login?federatedError=1";

    /// <summary>Mapeia o callback do login federado.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapFederatedLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/login/entra-callback", CallbackAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext context,
        EntraSignInProcessor processor,
        SignInManager<User> signInManager,
        string? returnUrl)
    {
        var authentication = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);

        if (!authentication.Succeeded || authentication.Principal is null)
        {
            return Results.Redirect(GenericFailureRedirect);
        }

        var result = await processor.ProcessAsync(authentication.Principal, context.RequestAborted);

        // O cookie externo é transitório: consumido aqui, some nos dois desfechos
        await context.SignOutAsync(IdentityConstants.ExternalScheme);

        if (result.IsFailure)
        {
            return Results.Redirect(GenericFailureRedirect);
        }

        // Sessão do Identity — daqui em diante o fluxo é o mesmo do login por senha:
        // /connect/authorize retoma e o SecureGate emite os tokens da plataforma (ADR-0026)
        await signInManager.SignInAsync(result.Value, isPersistent: false);

        // Só destinos locais — sem open redirect (ADR-0020)
        return Results.LocalRedirect(ToLocalUrl(returnUrl));
    }

    private static string ToLocalUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
            && returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
        ? returnUrl
        : "/";
}
