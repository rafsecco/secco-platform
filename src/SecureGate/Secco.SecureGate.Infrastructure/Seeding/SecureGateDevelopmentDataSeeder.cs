using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Infrastructure.Seeding;

/// <summary>
/// Seed de DESENVOLVIMENTO (ADR-0019, guarda dupla): um tenant demo (o mesmo Guid dos
/// appsettings de DEV dos produtos) e um client OIDC de console com secret conhecido —
/// jamais chega a produção (a orquestração do SDK garante).
/// </summary>
public sealed class SecureGateDevelopmentDataSeeder(
    SecureGateDbContext context,
    IOpenIddictApplicationManager applicationManager) : IDevelopmentDataSeeder
{
    /// <summary>Tenant demo — mesmo Guid usado nos appsettings.Development dos produtos.</summary>
    public static readonly Guid DemoTenantId = Guid.Parse("018f0000-0000-7000-8000-000000000001");

    /// <summary>Client de desenvolvimento para chamadas de console/testes manuais.</summary>
    public const string DevClientId = "secco-dev-console";

    /// <summary>Secret do client de desenvolvimento (conhecido — só existe em DEV).</summary>
    public const string DevClientSecret = "secco-dev-console-secret-32-chars-min!";

    /// <summary>Role demo com as permissões do LogStream (Fase 6.4) — atribuído ao client de DEV.</summary>
    public const string DevRoleName = "dev-admin";

    /// <summary>Permissões do role demo (strings literais: as constantes vivem em cada produto, ADR-0003).</summary>
    private static readonly string[] DevRolePermissions =
    [
        "log-entries:read", "log-entries:write",
        "log-processes:read", "log-processes:write",
        "api-call-logs:read", "api-call-logs:write",
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!await context.Tenants.AnyAsync(t => t.Id == DemoTenantId, cancellationToken).ConfigureAwait(false))
        {
            var tenant = new Tenant("Tenant de Desenvolvimento", "dev-alfa");
            context.Entry(tenant).Property(nameof(Tenant.Id)).CurrentValue = DemoTenantId;
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await SeedDevRoleAsync(cancellationToken).ConfigureAwait(false);

        if (await applicationManager.FindByClientIdAsync(DevClientId, cancellationToken).ConfigureAwait(false) is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = DevClientId,
                ClientSecret = DevClientSecret,
                DisplayName = "Console de desenvolvimento Secco",
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + "logstream",
                    Permissions.Prefixes.Scope + "securegate",
                    // Fase 6.3 — console de DEV também exercita catálogo e gestão localmente
                    Permissions.Prefixes.Scope + Application.SecureGateScopes.CatalogFor("logstream"),
                    Permissions.Prefixes.Scope + Application.SecureGateScopes.Admin,
                    // Fase 6.4 — e a resolução role→permissions
                    Permissions.Prefixes.Scope + Application.SecureGateScopes.AuthorizationRead,
                },
            }, cancellationToken).ConfigureAwait(false);
        }

        // Fase 6.4 (ADR-0021): o console de DEV carrega o role demo na claim curta 'role'
        if (await applicationManager.FindByClientIdAsync(DevClientId, cancellationToken).ConfigureAwait(false)
            is OpenIddict.OidcApplication { Roles: null or "" } devClient)
        {
            devClient.Roles = DevRoleName;
            await applicationManager.UpdateAsync(devClient, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Role demo do tenant de desenvolvimento com as permissões do LogStream.</summary>
    private async Task SeedDevRoleAsync(CancellationToken cancellationToken)
    {
        var normalized = DevRoleName.ToUpperInvariant();

        var role = await context.Roles
            .FirstOrDefaultAsync(r => r.TenantId == DemoTenantId && r.NormalizedName == normalized, cancellationToken)
            .ConfigureAwait(false);

        if (role is not null)
        {
            return;
        }

        role = new Identity.Role
        {
            Id = Guid.CreateVersion7(),
            TenantId = DemoTenantId,
            Name = DevRoleName,
            NormalizedName = normalized,
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };

        context.Roles.Add(role);
        context.RoleClaims.AddRange(DevRolePermissions.Select(permission => new Identity.RoleClaim
        {
            RoleId = role.Id,
            ClaimType = Roles.RoleRepository.PermissionClaimType,
            ClaimValue = permission,
        }));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
