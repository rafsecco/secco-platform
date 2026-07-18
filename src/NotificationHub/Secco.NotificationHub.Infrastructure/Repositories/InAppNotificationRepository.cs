using Microsoft.EntityFrameworkCore;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.NotificationHub.Domain.InAppNotifications;
using Secco.NotificationHub.Infrastructure.Contexts;

namespace Secco.NotificationHub.Infrastructure.Repositories;

/// <summary>Persistência do inbox in-app no banco do tenant atual.</summary>
internal sealed class InAppNotificationRepository(NotificationHubDbContext context) : IInAppNotificationRepository
{
    public async Task AddAsync(InAppNotification notification, CancellationToken cancellationToken = default)
    {
        context.InAppNotifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<InAppNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.InAppNotifications
            .FirstOrDefaultAsync(notification => notification.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<InAppNotification>> GetUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.InAppNotifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.InAppNotifications
            .AsNoTracking()
            .CountAsync(notification => notification.UserId == userId && !notification.IsRead, cancellationToken);

    public async Task UpdateAsync(InAppNotification notification, CancellationToken cancellationToken = default)
    {
        context.InAppNotifications.Update(notification);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
