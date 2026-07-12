using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Secco.LogStream.Infrastructure.Contexts;

namespace Secco.LogStream.Migrations.Postgres;

/// <summary>
/// Fábrica de design-time do <c>dotnet ef</c> para migrations PostgreSQL
/// (connection string fictícia: geração de migration não conecta ao banco).
/// </summary>
public sealed class LogStreamPostgresDbContextFactory : IDesignTimeDbContextFactory<LogStreamDbContext>
{
    public LogStreamDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LogStreamDbContext>()
            .UseNpgsql(
                "Host=design-time;Database=design-time",
                npgsql => npgsql.MigrationsAssembly(typeof(LogStreamPostgresDbContextFactory).Assembly.GetName().Name))
            .Options);
}
