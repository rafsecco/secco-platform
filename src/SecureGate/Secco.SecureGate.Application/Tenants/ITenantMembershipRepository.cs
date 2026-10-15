using Secco.SecureGate.Domain.Tenants;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>Persistência dos vínculos pessoa ↔ tenant (ADR-0040).</summary>
public interface ITenantMembershipRepository
{
	/// <summary>Estado do vínculo; <c>null</c> quando a pessoa não é membro do tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantMembership?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Se a pessoa tem vínculo ATIVO no tenant (a condição de emitir token).</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> IsActiveMemberAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Adiciona um vínculo ao contexto (efetivado no <see cref="SaveChangesAsync"/>).</summary>
	/// <param name="membership">Vínculo a adicionar.</param>
	void Add(TenantMembership membership);

	/// <summary>
	/// Adiciona e efetiva o vínculo, tratando a corrida: se outra chamada criou o mesmo par antes, devolve
	/// <c>false</c> em vez de falhar na chave primária (ADR-0034).
	/// </summary>
	/// <param name="membership">Vínculo a adicionar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se o vínculo já existia.</returns>
	Task<bool> TryAddAsync(TenantMembership membership, CancellationToken cancellationToken = default);

	/// <summary>
	/// Remove o vínculo E as atribuições de perfil da pessoa naquele tenant, numa transação —
	/// reincluí-la depois não pode devolver acesso esquecido (ADR-0040). Perfis de outros tenants
	/// ficam como estão.
	/// </summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>true</c> se havia vínculo.</returns>
	Task<bool> RemoveAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Efetiva as alterações pendentes.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
