using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Secco.LogStream.Infrastructure.Contexts;

namespace Secco.LogStream.Migrations.SqlServer;

/// <summary>
/// Fábrica de design-time do <c>dotnet ef</c> para migrations SQL Server
/// (connection string fictícia: geração de migration não conecta ao banco).
/// </summary>
public sealed class LogStreamSqlServerDbContextFactory : IDesignTimeDbContextFactory<LogStreamDbContext>
{
    public LogStreamDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LogStreamDbContext>()
            .UseSqlServer(
                "Server=design-time;Database=design-time;Encrypt=false",
                sql => sql.MigrationsAssembly(typeof(LogStreamSqlServerDbContextFactory).Assembly.GetName().Name))
            .Options);
}
