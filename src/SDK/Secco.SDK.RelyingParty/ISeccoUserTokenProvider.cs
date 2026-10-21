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
/// Quando e como a pessoa logada se autenticou (ADR-0050), lido do access token custodiado — não das claims do
/// cookie. É o que a política de autenticação recente consulta.
/// </summary>
public interface ISeccoUserAuthentication
{
	/// <summary>
	/// Autenticação da sessão atual (renovando o token se preciso); <c>null</c> quando não há sessão. Token opaco
	/// ou sem <c>auth_time</c>/<c>amr</c> dá <c>AuthenticatedAt</c> nulo e métodos vazios.
	/// </summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<SeccoAuthentication?> GetAuthenticationAsync(CancellationToken cancellationToken = default);
}

/// <summary>Sessão resolvida (já renovada se preciso) com o id do cofre; uso interno do pacote.</summary>
internal sealed record SeccoResolvedSession(string SessionId, SeccoUserSession Session);

/// <summary>Acesso interno à sessão do cofre, com renovação.</summary>
internal interface ISeccoSessionResolver
{
	/// <summary>Sessão atual com seu id, ou <c>null</c> sem sessão.</summary>
	Task<SeccoResolvedSession?> ResolveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Trava por sessão, compartilhada por quem escreve no cofre (renovação e troca): um conjunto fixo escolhido
/// pelo hash da sessão — sessões diferentes podem dividir uma trava por instantes, mas a memória não cresce
/// com cada login.
/// </summary>
internal static class SeccoSessionLocks
{
	private const int LockStripes = 64;

	private static readonly SemaphoreSlim[] Locks = [.. Enumerable.Range(0, LockStripes).Select(_ => new SemaphoreSlim(1, 1))];

	public static SemaphoreSlim For(string sessionId) => Locks[(uint)StringComparer.Ordinal.GetHashCode(sessionId) % LockStripes];
}

/// <summary>
/// Renova uma vez por sessão: o refresh token é rotativo, e duas renovações simultâneas derrubariam a segunda.
/// A exclusão é POR INSTÂNCIA da aplicação (semáforo em memória do processo); com várias réplicas, o emissor
/// ainda recusa o refresh já usado e a perdedora cai em novo login — raro e seguro (fail-closed). As travas são
/// um conjunto fixo escolhido pelo hash da sessão: sessões diferentes podem dividir uma trava por instantes, mas
/// a memória não cresce com cada login, como cresceria um dicionário por sessão que ninguém limpa.
/// </summary>
internal sealed class SeccoUserTokenProvider(
	ISeccoUserContext userContext,
	ISeccoUserSessionStore store,
	ISeccoTokenRefresher refresher,
	IOptions<SeccoRelyingPartyOptions> options) : ISeccoUserTokenProvider, ISeccoUserAuthentication, ISeccoSessionResolver
{
	public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
		(await ResolveSessionAsync(cancellationToken).ConfigureAwait(false))?.AccessToken;

	public async Task<IReadOnlyList<string>> GetGrantedScopesAsync(CancellationToken cancellationToken = default) =>
		(await ResolveSessionAsync(cancellationToken).ConfigureAwait(false))?.Scopes ?? [];

	public async Task<bool> HasScopeAsync(string scope, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(scope);

		return (await GetGrantedScopesAsync(cancellationToken).ConfigureAwait(false)).Contains(scope, StringComparer.Ordinal);
	}

	public async Task<SeccoAuthentication?> GetAuthenticationAsync(CancellationToken cancellationToken = default) =>
		await ResolveSessionAsync(cancellationToken).ConfigureAwait(false) is { } session
			? new SeccoAuthentication(session.AuthenticatedAt, session.AuthenticationMethods)
			: null;

	async Task<SeccoResolvedSession?> ISeccoSessionResolver.ResolveAsync(CancellationToken cancellationToken)
	{
		var user = await userContext.GetUserAsync().ConfigureAwait(false);
		var session = await ResolveSessionAsync(cancellationToken).ConfigureAwait(false);

		return session is not null && user?.FindFirst(options.Value.SessionIdClaim)?.Value is { Length: > 0 } sessionId
			? new SeccoResolvedSession(sessionId, session)
			: null;
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

		var gate = SeccoSessionLocks.For(sessionId);
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

			// Os tokens trocados sobrevivem à renovação do token da pessoa (ADR-0050)
			renewed = renewed with { ExchangedTokens = session.ExchangedTokens };

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
