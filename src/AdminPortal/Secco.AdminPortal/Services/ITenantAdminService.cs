namespace Secco.AdminPortal.Services;

/// <summary>Gestão de tenants a partir do AdminPortal, on-behalf-of o operador (ADR-0023).</summary>
public interface ITenantAdminService
{
    /// <summary>Lista os tenants do catálogo via <c>Secco.SecureGate.Client</c>.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<TenantSummary>> ListTenantsAsync(CancellationToken cancellationToken = default);
}
