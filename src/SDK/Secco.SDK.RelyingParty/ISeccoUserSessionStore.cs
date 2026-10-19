using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Secco.SDK.RelyingParty;

/// <summary>
/// Cofre de sessões da pessoa, na chave de um id aleatório guardado no cookie (ADR-0046). A porta é pública
/// para quem precisar de outro armazenamento; o padrão usa <see cref="IDistributedCache"/>.
/// </summary>
public interface ISeccoUserSessionStore
{
	/// <summary>Sessão, ou <c>null</c> se expirou ou o armazenamento foi perdido (reinício, por exemplo).</summary>
	/// <param name="sessionId">Id da sessão, vindo da claim do cookie.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<SeccoUserSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

	/// <summary>Grava ou substitui a sessão.</summary>
	/// <param name="sessionId">Id da sessão.</param>
	/// <param name="session">Tokens a guardar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SetAsync(string sessionId, SeccoUserSession session, CancellationToken cancellationToken = default);

	/// <summary>Descarta a sessão.</summary>
	/// <param name="sessionId">Id da sessão.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cofre sobre <see cref="IDistributedCache"/> (ADR-0035): em memória por padrão; mais de uma instância da
/// aplicação exige cache distribuído de verdade. O cache passa a guardar credencial — entra no controle de
/// acesso e no backup com esse peso.
/// </summary>
internal sealed class DistributedSeccoUserSessionStore(IDistributedCache cache, IOptions<SeccoRelyingPartyOptions> options)
	: ISeccoUserSessionStore
{
	private const string KeyPrefix = "secco:relyingparty:session:";

	private readonly DistributedCacheEntryOptions _entry = new() { SlidingExpiration = options.Value.SessionLifetime };

	public async Task<SeccoUserSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default) =>
		await cache.GetAsync(KeyPrefix + sessionId, cancellationToken).ConfigureAwait(false) is { } bytes
			? JsonSerializer.Deserialize<SeccoUserSession>(bytes)
			: null;

	public Task SetAsync(string sessionId, SeccoUserSession session, CancellationToken cancellationToken = default) =>
		cache.SetAsync(KeyPrefix + sessionId, JsonSerializer.SerializeToUtf8Bytes(session), _entry, cancellationToken);

	public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default) =>
		cache.RemoveAsync(KeyPrefix + sessionId, cancellationToken);
}
