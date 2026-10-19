using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.TenantAdministration;

namespace Secco.SecureGate.Application.TenantAdministration;

/// <summary>Admin de um tenant, como a gestão das concessões o mostra (ADR-0044).</summary>
/// <param name="UserId">Pessoa.</param>
/// <param name="Email">E-mail da pessoa.</param>
/// <param name="DisplayName">Nome de exibição, se houver.</param>
/// <param name="OwnerTenantId">Tenant dono da conta (ADR-0040) — pode não ser o administrado.</param>
/// <param name="GrantedBy">Sub de quem concedeu.</param>
/// <param name="CreatedAt">Momento da concessão.</param>
public sealed record TenantAdminDto(
	Guid UserId,
	string Email,
	string? DisplayName,
	Guid OwnerTenantId,
	string GrantedBy,
	DateTimeOffset CreatedAt);

/// <summary>
/// Persistência e consulta das concessões de admin de tenant (ADR-0044). Os métodos que olham a pessoa
/// ou atravessam tenants entram no modo plataforma por conta própria (ADR-0041): a pessoa pode ser dona
/// de outro tenant e nem ser membro do administrado.
/// </summary>
public interface ITenantAdminGrantRepository
{
	/// <summary>Indica se a concessão (pessoa, tenant) existe. Chamado no escopo do tenant administrado.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> ExistsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Insere a concessão. Chamado no escopo do tenant administrado.</summary>
	/// <param name="grant">Concessão nova.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(TenantAdminGrant grant, CancellationToken cancellationToken = default);

	/// <summary>Remove a concessão, se existir. Chamado no escopo do tenant administrado.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>true</c> se havia concessão.</returns>
	Task<bool> RemoveAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Lista os admins do tenant, com os dados da pessoa (lidos em modo plataforma).</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantAdminDto>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Indica se a pessoa existe na instalação, em qualquer tenant (modo plataforma).</summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Indica se a pessoa tem ao menos uma concessão, em qualquer tenant (modo plataforma).</summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> HasAnyAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Indica se a pessoa tem concessão SOBRE o tenant, em modo plataforma — usado na conferência de
	/// cada requisição, que roda antes e fora do escopo do tenant da rota.
	/// </summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> HasGrantAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Os tenants que a pessoa administra, ordenados por nome (modo plataforma).</summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantDto>> ListTenantsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
