namespace Secco.AdminPortal.Services;

/// <summary>Gestão de tenants a partir do AdminPortal, on-behalf-of o operador (ADR-0023).</summary>
public interface ITenantAdminService
{
	/// <summary>Lista os tenants do catálogo.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantSummary>> ListTenantsAsync(CancellationToken cancellationToken = default);

	/// <summary>Busca o detalhe de um tenant (cabeçalho da tela de gestão).</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<TenantDetail> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Cadastra ou rotaciona o banco de um tenant num produto (PUT idempotente, Fase 7.4).
	/// Write-only: a connection string nunca volta em nenhuma leitura (ADR-0020).
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="product">Produto (kebab-case).</param>
	/// <param name="connectionString">Connection string do banco dedicado. Nunca logar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task UpsertDatabaseAsync(
		Guid tenantId, string product, string connectionString, CancellationToken cancellationToken = default);

	/// <summary>Lista as pessoas com concessão de admin do tenant (ADR-0044).</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantAdminSummary>> ListAdminsAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Os tenants que a pessoa administra. Não há rota "por pessoa" para o operador: consulta os admins de
	/// cada tenant do catálogo — custo proporcional ao número de tenants, aceitável numa tela de gestão.
	/// </summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<TenantSummary>> ListAdministeredByAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Concede a autoridade de admin do tenant à pessoa (PUT idempotente, ADR-0044).</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="userId">Pessoa, de qualquer tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task GrantAdminAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Revoga a autoridade de admin do tenant (DELETE idempotente; vale na próxima requisição da pessoa).</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RevokeAdminAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
}
