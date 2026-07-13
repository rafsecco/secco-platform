using Microsoft.EntityFrameworkCore.Design;
using Secco.SecureGate.Infrastructure;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Migrations.Postgres;

/// <summary>
/// Fábrica de design-time do <c>dotnet ef</c> para migrations PostgreSQL. Usa o
/// configurator central — inclui as entidades OpenIddict no modelo (o snapshot
/// jamais pode divergir do runtime).
/// </summary>
public sealed class SecureGatePostgresDbContextFactory : IDesignTimeDbContextFactory<SecureGateDbContext>
{
    public SecureGateDbContext CreateDbContext(string[] args) =>
        new(SecureGateDatabaseProviderConfigurator.CreateOptions(
            SecureGateDatabaseProvider.PostgreSql,
            "Host=design-time;Database=design-time"));
}
