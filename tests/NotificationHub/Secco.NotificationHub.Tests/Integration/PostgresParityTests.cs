using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Secco.NotificationHub.Domain.Notifications;
using Secco.NotificationHub.Infrastructure.Contexts;
using Secco.NotificationHub.Infrastructure.Repositories;
using Testcontainers.PostgreSql;
using Xunit;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Paridade do segundo provider (ADR-0018): as migrations do assembly próprio aplicam do
/// zero num PostgreSQL real, o schema é idêntico (minúsculo, sem aspas — ADR-0017) e o
/// repositório grava/lê corretamente. Não sobe a API/Hangfire: o Hangfire é SQL-Server-only
/// no v1 (armazenamento de fila é infraestrutura de plataforma, não dado de tenant) — esta
/// suíte prova só o que muda com o provider, o banco de tenant. A suíte completa (envio
/// real via Hangfire) segue no provider padrão (SQL Server, <see cref="NotificationEndpointsTests"/>).
/// </summary>
public sealed class PostgresParityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private NotificationHubDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<NotificationHubDbContext>()
            .UseNpgsql(_container.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly("Secco.NotificationHub.Migrations.Postgres"))
            .Options);

    [Fact]
    public async Task Migrations_OnPostgres_ApplyFromScratchWithUnquotedLowercaseSchema()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

        // Identificadores sem aspas resolvem — a promessa da nomenclatura minúscula (ADR-0017)
        var act = () => context.Database.ExecuteSqlRawAsync(
            "SELECT id_pk_notification, ds_recipient, ie_status FROM tb_notifications WHERE 1 = 0");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Repository_OnPostgres_RoundTripsNotificationCorrectly()
    {
        await using (var migrationContext = CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        var notificationId = Guid.NewGuid();

        await using (var writeContext = CreateContext())
        {
            var repository = new NotificationRepository(writeContext);
            var notification = new Notification("destinatario@teste.com", "Assunto", "Corpo");
            await repository.AddAsync(notification);
            notificationId = notification.Id;

            notification.MarkAsSent();
            await repository.UpdateAsync(notification);
        }

        await using var readContext = CreateContext();
        var fetched = await new NotificationRepository(readContext).GetByIdAsync(notificationId);

        fetched.Should().NotBeNull();
        fetched!.Status.Should().Be(NotificationStatus.Sent);
        fetched.SentAt.Should().NotBeNull();
    }
}
