using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Secco.SampleService.Infrastructure.Contexts;

namespace Secco.SampleService.Migrations.Postgres;

/// <summary>
/// Fábrica de design-time do <c>dotnet ef</c> para migrations PostgreSQL
/// (connection string fictícia: geração de migration não conecta ao banco).
/// </summary>
public sealed class SampleServicePostgresDbContextFactory : IDesignTimeDbContextFactory<SampleServiceDbContext>
{
    public SampleServiceDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SampleServiceDbContext>()
            .UseNpgsql(
                "Host=design-time;Database=design-time",
                npgsql => npgsql.MigrationsAssembly(typeof(SampleServicePostgresDbContextFactory).Assembly.GetName().Name))
            .Options);
}
