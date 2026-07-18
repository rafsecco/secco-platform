using Microsoft.EntityFrameworkCore;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.NotificationHub.Infrastructure.Contexts;

namespace Secco.NotificationHub.Infrastructure.Repositories;

/// <summary>Persistência de notificações no banco do tenant atual.</summary>
internal sealed class NotificationRepository(NotificationHubDbContext context) : INotificationRepository
{
    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        context.Notifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Notifications
            .FirstOrDefaultAsync(notification => notification.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        context.Notifications.Update(notification);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
