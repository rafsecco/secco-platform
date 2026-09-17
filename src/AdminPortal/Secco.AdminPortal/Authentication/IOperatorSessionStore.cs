using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Secco.AdminPortal.Authentication;

/// <summary>Cofre de sessões do operador, na chave de um id aleatório guardado no cookie.</summary>
public interface IOperatorSessionStore
{
	/// <summary>Sessão, ou <c>null</c> se expirou ou o AdminPortal reiniciou.</summary>
	Task<OperatorSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

	/// <summary>Grava ou substitui a sessão.</summary>
	Task SetAsync(string sessionId, OperatorSession session, CancellationToken cancellationToken = default);

	/// <summary>Descarta a sessão.</summary>
	Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cofre sobre <see cref="IDistributedCache"/> — em memória por padrão; mais de uma instância do AdminPortal
/// exige cache distribuído (ADR-0032).
/// </summary>
internal sealed class DistributedOperatorSessionStore(IDistributedCache cache) : IOperatorSessionStore
{
	private static readonly DistributedCacheEntryOptions Entry = new() { SlidingExpiration = TimeSpan.FromHours(8) };

	private static string Key(string sessionId) => "adminportal:session:" + sessionId;

	public async Task<OperatorSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default) =>
		await cache.GetAsync(Key(sessionId), cancellationToken).ConfigureAwait(false) is { } bytes
			? JsonSerializer.Deserialize<OperatorSession>(bytes)
			: null;

	public Task SetAsync(string sessionId, OperatorSession session, CancellationToken cancellationToken = default) =>
		cache.SetAsync(Key(sessionId), JsonSerializer.SerializeToUtf8Bytes(session), Entry, cancellationToken);

	public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default) =>
		cache.RemoveAsync(Key(sessionId), cancellationToken);
}
