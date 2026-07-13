using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Secco.SampleService.Infrastructure.Contexts;

namespace Secco.SampleService.Migrations.SqlServer;

/// <summary>
/// Fábrica de design-time do <c>dotnet ef</c> para migrations SQL Server
/// (connection string fictícia: geração de migration não conecta ao banco).
/// </summary>
public sealed class SampleServiceSqlServerDbContextFactory : IDesignTimeDbContextFactory<SampleServiceDbContext>
{
    public SampleServiceDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SampleServiceDbContext>()
            .UseSqlServer(
                "Server=design-time;Database=design-time;Encrypt=false",
                sql => sql.MigrationsAssembly(typeof(SampleServiceSqlServerDbContextFactory).Assembly.GetName().Name))
            .Options);
}
