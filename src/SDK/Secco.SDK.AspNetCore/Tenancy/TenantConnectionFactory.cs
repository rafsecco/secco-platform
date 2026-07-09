namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Implementação padrão de <see cref="ITenantConnectionFactory"/>: combina o
/// <see cref="ITenantContext"/> da requisição com o <see cref="ITenantCatalog"/>.
/// </summary>
internal sealed class TenantConnectionFactory(ITenantContext tenantContext, ITenantCatalog tenantCatalog)
    : ITenantConnectionFactory
{
    public async ValueTask<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            throw new TenantNotResolvedException();
        }

        var tenant = await tenantCatalog.FindAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new TenantNotFoundException(tenantId);

        return tenant.ConnectionString;
    }
}
