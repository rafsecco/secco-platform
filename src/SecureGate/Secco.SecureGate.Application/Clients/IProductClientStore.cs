namespace Secco.SecureGate.Application.Clients;

/// <summary>
/// Persistência dos clients de PRODUTO (ADR-0037). Toda operação filtra por tenant E por origem
/// <see cref="ClientOrigin.Api"/>: client de outro tenant ou de plataforma é invisível aqui.
/// </summary>
public interface IProductClientStore
{
	/// <summary>Indica se o nome já está em uso no tenant, ignorando o próprio client na alteração.</summary>
	Task<bool> NameExistsAsync(Guid tenantId, string name, string? exceptClientId, CancellationToken cancellationToken = default);

	/// <summary>Cria o client com o id e o secret informados (o secret persiste só como hash).</summary>
	Task<ProductClientDto> CreateAsync(
		Guid tenantId, string clientId, string clientSecret, ProductClientAccess access, CancellationToken cancellationToken = default);

	/// <summary>Lista os clients do tenant, por nome.</summary>
	Task<IReadOnlyList<ProductClientDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Busca um client do tenant; nulo se não existir aqui.</summary>
	Task<ProductClientDto?> GetAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default);

	/// <summary>Substitui nome, escopos e papéis. Falso se o client não existir no tenant.</summary>
	Task<bool> UpdateAsync(Guid tenantId, string clientId, ProductClientAccess access, CancellationToken cancellationToken = default);

	/// <summary>Troca o secret na hora. Falso se o client não existir no tenant.</summary>
	Task<bool> RotateSecretAsync(Guid tenantId, string clientId, string clientSecret, CancellationToken cancellationToken = default);

	/// <summary>Remove o client (e os tokens/autorizações do OpenIddict). Falso se não existir no tenant.</summary>
	Task<bool> DeleteAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default);
}
