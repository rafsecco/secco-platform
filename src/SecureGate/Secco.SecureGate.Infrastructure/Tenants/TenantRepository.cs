using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Tenants;

/// <summary>Persistência EF Core do catálogo de tenants sobre o banco de plataforma.</summary>
internal sealed class TenantRepository(SecureGateDbContext context) : ITenantRepository
{
    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
        await context.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default) =>
        await context.Tenants.AddAsync(tenant, cancellationToken).ConfigureAwait(false);

    public async Task<TenantDatabase?> GetDatabaseAsync(
        Guid tenantId, string product, CancellationToken cancellationToken = default) =>
        await context.TenantDatabases
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Product == product, cancellationToken)
            .ConfigureAwait(false);

    public async Task AddDatabaseAsync(TenantDatabase database, CancellationToken cancellationToken = default) =>
        await context.TenantDatabases.AddAsync(database, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> ListDatabaseProductsAsync(
        Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.TenantDatabases
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.Product)
            .Select(d => d.Product)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<TenantDatabase?> FindActiveDatabaseAsync(
        Guid tenantId, string product, CancellationToken cancellationToken = default) =>
        await context.TenantDatabases
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Product == product)
            .Where(d => context.Tenants.Any(t => t.Id == d.TenantId && t.IsActive))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<TenantDatabase>> ListActiveDatabasesAsync(
        string product, CancellationToken cancellationToken = default) =>
        await context.TenantDatabases
            .AsNoTracking()
            .Where(d => d.Product == product)
            .Where(d => context.Tenants.Any(t => t.Id == d.TenantId && t.IsActive))
            .OrderBy(d => d.TenantId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<TenantFederation?> GetFederationAsync(
        Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.TenantFederations
            .FirstOrDefaultAsync(f => f.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

    public async Task AddFederationAsync(TenantFederation federation, CancellationToken cancellationToken = default) =>
        await context.TenantFederations.AddAsync(federation, cancellationToken).ConfigureAwait(false);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
