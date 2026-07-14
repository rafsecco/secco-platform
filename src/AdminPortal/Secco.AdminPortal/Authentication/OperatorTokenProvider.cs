using Microsoft.AspNetCore.Components.Authorization;

namespace Secco.AdminPortal.Authentication;

/// <summary>
/// Lê o access token do operador a partir do <see cref="AuthenticationStateProvider"/> —
/// funciona tanto no prerender (SSR) quanto no circuito interativo do Blazor Server, onde
/// o principal do cookie está disponível (evita o problema de <c>IHttpContextAccessor</c>
/// nulo durante o render interativo).
/// </summary>
internal sealed class OperatorTokenProvider(AuthenticationStateProvider authenticationStateProvider)
    : IOperatorTokenProvider
{
    public async Task<string?> GetAccessTokenAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);

        return state.User.FindFirst(AdminPortalDefaults.AccessTokenClaim)?.Value;
    }
}
