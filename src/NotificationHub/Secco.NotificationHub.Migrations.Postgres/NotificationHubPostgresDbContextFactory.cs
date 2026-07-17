using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Secco.NotificationHub.Infrastructure.Contexts;

namespace Secco.NotificationHub.Migrations.Postgres;

/// <summary>
/// Fábrica de design-time do <c>dotnet ef</c> para migrations PostgreSQL
/// (connection string fictícia: geração de migration não conecta ao banco).
/// </summary>
public sealed class NotificationHubPostgresDbContextFactory : IDesignTimeDbContextFactory<NotificationHubDbContext>
{
    public NotificationHubDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationHubDbContext>()
            .UseNpgsql(
                "Host=design-time;Database=design-time",
                npgsql => npgsql.MigrationsAssembly(typeof(NotificationHubPostgresDbContextFactory).Assembly.GetName().Name))
            .Options);
}
