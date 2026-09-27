using System.Collections.Concurrent;

namespace Secco.SecureGate.Infrastructure.Federation;

/// <summary>
/// Cache do token de aplicação por diretório do Entra ID (ADR-0036) — cache derivado, TTL curto,
/// em memória de processo (classe 1 da ADR-0035). Um token de aplicação vale só para o diretório
/// que o emitiu, então a chave é o directory id, não um slot único como o
/// <c>SeccoAccessTokenStore</c> do <c>Secco.SDK.ClientCredentials</c> (que serve um único emissor).
/// </summary>
internal sealed class GraphAppTokenCache
{
	/// <summary>Margem antes da expiração para renovar o token proativamente.</summary>
	private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);

	private readonly ConcurrentDictionary<Guid, Entry> _tokens = new();
	private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

	/// <summary>
	/// Devolve o token válido do diretório, emitindo um novo se ausente ou perto de expirar.
	/// </summary>
	/// <param name="directoryId">Diretório alvo.</param>
	/// <param name="issue">Emite um token novo; só chamado quando necessário, sob lock por diretório.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<string> GetOrIssueAsync(
		Guid directoryId,
		Func<CancellationToken, Task<(string AccessToken, TimeSpan ExpiresIn)>> issue,
		CancellationToken cancellationToken)
	{
		if (TryGetValid(directoryId, out var cached))
		{
			return cached;
		}

		var gate = _locks.GetOrAdd(directoryId, static _ => new SemaphoreSlim(1, 1));

		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (TryGetValid(directoryId, out var refreshed))
			{
				return refreshed;
			}

			var (accessToken, expiresIn) = await issue(cancellationToken).ConfigureAwait(false);

			_tokens[directoryId] = new Entry(accessToken, DateTimeOffset.UtcNow + expiresIn);

			return accessToken;
		}
		finally
		{
			gate.Release();
		}
	}

	private bool TryGetValid(Guid directoryId, out string accessToken)
	{
		if (_tokens.TryGetValue(directoryId, out var entry) && DateTimeOffset.UtcNow < entry.ExpiresAt - ExpiryMargin)
		{
			accessToken = entry.AccessToken;

			return true;
		}

		accessToken = string.Empty;

		return false;
	}

	private readonly record struct Entry(string AccessToken, DateTimeOffset ExpiresAt);
}
