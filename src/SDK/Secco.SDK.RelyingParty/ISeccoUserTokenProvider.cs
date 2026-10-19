using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Secco.SDK.RelyingParty;

/// <summary>
/// Fornece o access token da pessoa logada para chamadas on-behalf-of às APIs (ADR-0023, ADR-0046). O token
/// vive num cofre no servidor (ADR-0032); o cookie leva apenas o id da sessão — nunca o token.
/// </summary>
public interface ISeccoUserTokenProvider
{
	/// <summary>
	/// Access token válido da pessoa, renovando perto do vencimento; <c>null</c> quando não há sessão
	/// (anônimo) ou ela se perdeu — neste último caso a pessoa foi mandada para novo login.
	/// </summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

	/// <summary>Escopos concedidos ao token atual; vazio quando não há sessão.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<string>> GetGrantedScopesAsync(CancellationToken cancellationToken = default);

	/// <summary>Se o token atual foi emitido com o escopo pedido (comparação ordinal).</summary>
	/// <param name="scope">Escopo a procurar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> HasScopeAsync(string scope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Renova uma vez por sessão: o refresh token é rotativo, e duas renovações simultâneas derrubariam a segunda.
/// A exclusão é POR INSTÂNCIA da aplicação (semáforo em memória do processo); com várias réplicas, o emissor
/// ainda recusa o refresh já usado e a perdedora cai em novo login — raro e seguro (fail-closed).
/// </summary>
internal sealed class SeccoUserTokenProvider(
	ISeccoUserContext userContext,
	ISeccoUserSessionStore store,
	ISeccoTokenRefresher refresher,
	IOptions<SeccoRelyingPartyOptions> options) : ISeccoUserTokenProvider
{
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

	public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
		(await ResolveSessionAsync(cancellationToken).ConfigureAwait(false))?.AccessToken;

	public async Task<IReadOnlyList<string>> GetGrantedScopesAsync(CancellationToken cancellationToken = default) =>
		(await ResolveSessionAsync(cancellationToken).ConfigureAwait(false))?.Scopes ?? [];

	public async Task<bool> HasScopeAsync(string scope, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(scope);

		return (await GetGrantedScopesAsync(cancellationToken).ConfigureAwait(false)).Contains(scope, StringComparer.Ordinal);
	}

	private async Task<SeccoUserSession?> ResolveSessionAsync(CancellationToken cancellationToken)
	{
		var user = await userContext.GetUserAsync().ConfigureAwait(false);

		if (user?.FindFirst(options.Value.SessionIdClaim)?.Value is not { Length: > 0 } sessionId)
		{
			return null;
		}

		var session = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

		if (session is null)
		{
			return Reauthenticate();
		}

		if (IsFresh(session))
		{
			return session;
		}

		var gate = Locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			// Outra chamada pode ter renovado enquanto esta esperava
			session = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

			if (session is null)
			{
				return Reauthenticate();
			}

			if (IsFresh(session))
			{
				return session;
			}

			var renewed = await refresher.RefreshAsync(session, cancellationToken).ConfigureAwait(false);

			if (renewed is null)
			{
				await store.RemoveAsync(sessionId, cancellationToken).ConfigureAwait(false);
				return Reauthenticate();
			}

			await store.SetAsync(sessionId, renewed, cancellationToken).ConfigureAwait(false);
			return renewed;
		}
		finally
		{
			gate.Release();
		}
	}

	private bool IsFresh(SeccoUserSession session) =>
		session.ExpiresAt - DateTimeOffset.UtcNow > options.Value.RefreshMargin;

	private SeccoUserSession? Reauthenticate()
	{
		userContext.RequestReauthentication();
		return null;
	}
}
