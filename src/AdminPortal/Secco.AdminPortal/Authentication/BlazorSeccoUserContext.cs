using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Secco.SDK.RelyingParty;

namespace Secco.AdminPortal.Authentication;

/// <summary>
/// Pessoa do circuito do Blazor Server (sem <c>HttpContext</c> depois da primeira renderização) e, quando a
/// sessão se perde, recarga completa da página: a requisição HTTP passa pela validação do cookie, que rejeita a
/// sessão sem cofre e leva ao login.
/// </summary>
internal sealed class BlazorSeccoUserContext(
	AuthenticationStateProvider authenticationStateProvider,
	NavigationManager navigation) : ISeccoUserContext
{
	public async Task<ClaimsPrincipal?> GetUserAsync() =>
		(await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false)).User;

	public void RequestReauthentication() => navigation.NavigateTo(navigation.Uri, forceLoad: true);
}
