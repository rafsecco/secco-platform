namespace Secco.AdminPortal.Services;

/// <summary>Gestão de tenants via <c>Secco.SecureGate.Client</c> autenticado (on-behalf-of, ADR-0023).</summary>
internal sealed class SecureGateTenantAdminService(ISecureGateClientFactory clientFactory) : ITenantAdminService
{
    public async Task<IReadOnlyList<TenantSummary>> ListTenantsAsync(CancellationToken cancellationToken = default)
    {
        var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        var tenants = await client.ListTenantsAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. tenants.Select(tenant => new TenantSummary(
                tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt))
        ];
    }

    public async Task<TenantDetail> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        var tenant = await client.GetTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return new TenantDetail(
            tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt, [.. tenant.Products]);
    }
}
