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

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!await context.Tenants.AnyAsync(t => t.Id == DemoTenantId, cancellationToken).ConfigureAwait(false))
        {
            var tenant = new Tenant("Tenant de Desenvolvimento", "dev-alfa");
            context.Entry(tenant).Property(nameof(Tenant.Id)).CurrentValue = DemoTenantId;
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

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
                },
            }, cancellationToken).ConfigureAwait(false);
        }
    }
}
