using Secco.SecureGate.Domain.Tenants;

namespace Secco.SecureGate.Application.Federation;

/// <summary>Mapeamento com o nome do perfil já resolvido, para a tela de gestão (issue #28).</summary>
/// <param name="Id">Identificador do mapeamento.</param>
/// <param name="EntraGroupId">Id do grupo no Entra ID.</param>
/// <param name="EntraGroupDisplayName">Nome do grupo, snapshot do cadastro.</param>
/// <param name="RoleId">Perfil concedido.</param>
/// <param name="RoleName">Nome do perfil concedido.</param>
/// <param name="CreatedAt">Momento do cadastro.</param>
/// <param name="IsProvisioningGroup">
/// Este é o grupo de provisionamento do tenant (ADR-0055). Entra NO FIM: o record é posicional e consumido pelo
/// client NSwag — inserir no meio renumeraria os campos existentes.
/// </param>
public sealed record GroupRoleMappingRecord(
	Guid Id,
	Guid EntraGroupId,
	string EntraGroupDisplayName,
	Guid RoleId,
	string RoleName,
	DateTimeOffset CreatedAt,
	bool IsProvisioningGroup = false);

/// <summary>Persistência dos mapeamentos grupo→perfil do diretório federado (ADR-0036).</summary>
public interface IGroupRoleMappingRepository
{
	/// <summary>Busca um mapeamento pelo grupo do Entra ID, dentro do tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="entraGroupId">Id do grupo no Entra ID.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantGroupRoleMapping?> FindByGroupAsync(
		Guid tenantId, Guid entraGroupId, CancellationToken cancellationToken = default);

	/// <summary>Busca um mapeamento pelo identificador, dentro do tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="mappingId">Identificador do mapeamento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantGroupRoleMapping?> GetByIdAsync(
		Guid tenantId, Guid mappingId, CancellationToken cancellationToken = default);

	/// <summary>O mapeamento marcado como grupo de provisionamento do tenant, se houver (ADR-0055).</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantGroupRoleMapping?> FindProvisioningAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Efetiva as alterações de marcação do grupo de provisionamento. O banco garante um só por tenant: se outro
	/// pedido marcou um grupo no meio, a violação do índice único vira <c>false</c> (e o contexto é limpo).
	/// </summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se já existe outro grupo de provisionamento no tenant.</returns>
	Task<bool> TrySaveProvisioningChangeAsync(CancellationToken cancellationToken = default);

	/// <summary>Lista os mapeamentos do tenant, com o nome do perfil já resolvido.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<GroupRoleMappingRecord>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Adiciona um mapeamento ao contexto (efetivado no <see cref="SaveChangesAsync"/>).</summary>
	/// <param name="mapping">Mapeamento a adicionar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(TenantGroupRoleMapping mapping, CancellationToken cancellationToken = default);

	/// <summary>Remove um mapeamento do contexto (efetivado no <see cref="SaveChangesAsync"/>).</summary>
	/// <param name="mapping">Mapeamento a remover.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemoveAsync(TenantGroupRoleMapping mapping, CancellationToken cancellationToken = default);

	/// <summary>Efetiva as alterações pendentes.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
