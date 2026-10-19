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

/// <summary>Pessoa com concessão de criação de tenant (ADR-0047).</summary>
/// <param name="UserId">Pessoa.</param>
/// <param name="Email">E-mail da pessoa.</param>
/// <param name="DisplayName">Nome de exibição, se houver.</param>
/// <param name="OwnerTenantId">Tenant dono da conta.</param>
/// <param name="GrantedBy">Sub de quem concedeu.</param>
/// <param name="CreatedAt">Momento da concessão.</param>
/// <param name="CreatedTenants">Quantos tenants ela criou e administra por isso.</param>
public sealed record TenantCreatorDto(
	Guid UserId,
	string Email,
	string? DisplayName,
	Guid OwnerTenantId,
	string GrantedBy,
	DateTimeOffset CreatedAt,
	int CreatedTenants);

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

	/// <summary>
	/// Indica se a pessoa tem alguma autoridade de admin de tenant: concessão de admin em qualquer tenant ou
	/// concessão de criação (ADR-0047). É o que decide se o escopo sai no login (modo plataforma).
	/// </summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> HasAnyAuthorityAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Indica se a pessoa tem a concessão de criação de tenant (ADR-0047).</summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> HasCreatorGrantAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Quantos tenants a pessoa criou e ainda administra por isso (modo plataforma, ADR-0047).</summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<int> CountCreatedTenantsAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Indica se o tenant é dono de alguma conta administrativa (modo plataforma, ADR-0047).</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> OwnsAdministrativeAccountAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Concede a criação de tenant, se ainda não houver (idempotente).</summary>
	/// <param name="grant">Concessão nova.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddCreatorGrantIfMissingAsync(TenantCreatorGrant grant, CancellationToken cancellationToken = default);

	/// <summary>Remove a concessão de criação, se existir.</summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemoveCreatorGrantAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Lista as pessoas com concessão de criação, com os dados da pessoa (modo plataforma).</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantCreatorDto>> ListCreatorsAsync(CancellationToken cancellationToken = default);

	/// <summary>Indica se a pessoa tem ao menos uma concessão, em qualquer tenant (modo plataforma).</summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> HasAnyAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Indica se a pessoa é conta administrativa — tem alguma concessão de admin de tenant ou de criação
	/// (ADR-0047) OU é dona no tenant de plataforma (operadores). Contra ela, a autoridade de admin de tenant não faz operação de
	/// pessoa nem nada que revogue as sessões dela (modo plataforma).
	/// </summary>
	/// <param name="userId">Pessoa alvo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> IsAdministrativeAccountAsync(Guid userId, CancellationToken cancellationToken = default);

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
