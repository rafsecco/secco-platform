using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Secco.NotificationHub.Infrastructure.Contexts;

namespace Secco.NotificationHub.Migrations.SqlServer;

/// <summary>
/// Fábrica de design-time do <c>dotnet ef</c> para migrations SQL Server
/// (connection string fictícia: geração de migration não conecta ao banco).
/// </summary>
public sealed class NotificationHubSqlServerDbContextFactory : IDesignTimeDbContextFactory<NotificationHubDbContext>
{
    public NotificationHubDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationHubDbContext>()
            .UseSqlServer(
                "Server=design-time;Database=design-time;Encrypt=false",
                sql => sql.MigrationsAssembly(typeof(NotificationHubSqlServerDbContextFactory).Assembly.GetName().Name))
            .Options);
}
