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
}
