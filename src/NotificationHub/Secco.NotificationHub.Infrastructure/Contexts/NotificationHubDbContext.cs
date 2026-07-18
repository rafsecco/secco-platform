using Microsoft.EntityFrameworkCore;
using Secco.NotificationHub.Domain.InAppNotifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SDK.EntityFrameworkCore;

namespace Secco.NotificationHub.Infrastructure.Contexts;

/// <summary>
/// Contexto de dados sobre o banco do tenant atual (ADR-0005) — a connection string vem
/// do <c>ITenantConnectionFactory</c> a cada requisição. Herda de <see cref="SeccoDbContext"/>:
/// nomenclatura da ADR-0017 aplicada por convention — ninguém digita nomes de coluna.
/// </summary>
public sealed class NotificationHubDbContext(DbContextOptions<NotificationHubDbContext> options)
    : SeccoDbContext(options)
{
    /// <summary>Notificações por e-mail (tabela <c>tb_notifications</c>).</summary>
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>Inbox in-app (tabela <c>tb_in_app_notifications</c>, Fase 8.4).</summary>
    public DbSet<InAppNotification> InAppNotifications => Set<InAppNotification>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationHubDbContext).Assembly);
    }
}
