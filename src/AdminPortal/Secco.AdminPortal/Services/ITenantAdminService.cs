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
}
