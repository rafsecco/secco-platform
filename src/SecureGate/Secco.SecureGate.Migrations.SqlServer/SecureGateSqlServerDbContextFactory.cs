using Microsoft.EntityFrameworkCore.Design;
using Secco.SecureGate.Infrastructure;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Migrations.SqlServer;

/// <summary>
/// Fábrica de design-time do <c>dotnet ef</c> para migrations SQL Server. Usa o
/// configurator central — inclui as entidades OpenIddict no modelo (o snapshot
/// jamais pode divergir do runtime).
/// </summary>
public sealed class SecureGateSqlServerDbContextFactory : IDesignTimeDbContextFactory<SecureGateDbContext>
{
    public SecureGateDbContext CreateDbContext(string[] args) =>
        new(SecureGateDatabaseProviderConfigurator.CreateOptions(
            SecureGateDatabaseProvider.SqlServer,
            "Server=design-time;Database=design-time;Encrypt=false"));
}
