using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Secco.AdminPortal.Authentication;

/// <summary>
/// Access token do operador a partir do cofre (ADR-0032): renova quando faltam menos de 60 s, uma renovação por
/// sessão — o refresh token é rotativo, e duas renovações simultâneas derrubariam a segunda.
/// </summary>
internal sealed class OperatorTokenProvider(
	AuthenticationStateProvider authenticationStateProvider,
	IOperatorSessionStore store,
	IOperatorTokenRefresher refresher,
	NavigationManager navigation) : IOperatorTokenProvider
{
	private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

	private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

	public async Task<string?> GetAccessTokenAsync()
	{
		var state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);

		if (state.User.FindFirst(AdminPortalDefaults.SessionIdClaim)?.Value is not { Length: > 0 } sessionId)
		{
			return null;
		}

		var session = await store.GetAsync(sessionId).ConfigureAwait(false);

		if (session is null)
		{
			return Reauthenticate();
		}

		if (session.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
		{
			return session.AccessToken;
		}

		var gate = Locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync().ConfigureAwait(false);

		try
		{
			// Outra chamada pode ter renovado enquanto esta esperava
			session = await store.GetAsync(sessionId).ConfigureAwait(false);

			if (session is null)
			{
				return Reauthenticate();
			}

			if (session.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
			{
				return session.AccessToken;
			}

			var renewed = await refresher.RefreshAsync(session.RefreshToken).ConfigureAwait(false);

			if (renewed is null)
			{
				await store.RemoveAsync(sessionId).ConfigureAwait(false);
				return Reauthenticate();
			}

			await store.SetAsync(sessionId, renewed).ConfigureAwait(false);
			return renewed.AccessToken;
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	/// Recarga completa da página: a requisição HTTP passa pela validação do cookie, que rejeita a sessão sem
	/// cofre e leva ao login.
	/// </summary>
	private string? Reauthenticate()
	{
		navigation.NavigateTo(navigation.Uri, forceLoad: true);
		return null;
	}
}
