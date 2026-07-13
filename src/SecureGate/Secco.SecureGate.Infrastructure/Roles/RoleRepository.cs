using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Infrastructure.Roles;

/// <summary>
/// Persistência EF Core de roles e permissões sobre o modelo Identity (ADR-0021):
/// role por tenant em <c>tb_roles</c>, permissões como claims de ação em
/// <c>tb_role_claims</c> (<see cref="PermissionClaimType"/>).
/// </summary>
internal sealed class RoleRepository(SecureGateDbContext context) : IRoleRepository
{
    /// <summary>Tipo de claim de role reservado às permissões da ADR-0021.</summary>
    internal const string PermissionClaimType = "permission";

    public async Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.Tenants.AnyAsync(t => t.Id == tenantId, cancellationToken).ConfigureAwait(false);

    public async Task<bool> RoleExistsAsync(Guid tenantId, string name, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(name);

        return await context.Roles
            .AnyAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CreateRoleAsync(Guid tenantId, string name, CancellationToken cancellationToken = default)
    {
        context.Roles.Add(new Role
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Name = name,
            NormalizedName = Normalize(name),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var roles = await context.Roles
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Name,
                Permissions = context.RoleClaims
                    .Where(c => c.RoleId == r.Id && c.ClaimType == PermissionClaimType && c.ClaimValue != null)
                    .OrderBy(c => c.ClaimValue)
                    .Select(c => c.ClaimValue!)
                    .ToList(),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return [.. roles.Select(r => new RoleDto(r.Name!, r.Permissions))];
    }

    public async Task<IReadOnlyList<string>?> GetPermissionsAsync(
        Guid tenantId, string name, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(name);

        var role = await context.Roles
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.NormalizedName == normalized)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (role is null)
        {
            return null;
        }

        return await context.RoleClaims
            .AsNoTracking()
            .Where(c => c.RoleId == role.Id && c.ClaimType == PermissionClaimType && c.ClaimValue != null)
            .OrderBy(c => c.ClaimValue)
            .Select(c => c.ClaimValue!)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ReplacePermissionsAsync(
        Guid tenantId,
        string name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(name);

        var role = await context.Roles
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized, cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return false;
        }

        var existing = await context.RoleClaims
            .Where(c => c.RoleId == role.Id && c.ClaimType == PermissionClaimType)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        context.RoleClaims.RemoveRange(existing);
        context.RoleClaims.AddRange(permissions.Select(permission => new RoleClaim
        {
            RoleId = role.Id,
            ClaimType = PermissionClaimType,
            ClaimValue = permission,
        }));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static string Normalize(string name) => name.ToUpperInvariant();
}
