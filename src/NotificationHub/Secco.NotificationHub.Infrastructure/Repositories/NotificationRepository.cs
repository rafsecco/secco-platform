using Microsoft.EntityFrameworkCore;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.NotificationHub.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.NotificationHub.Infrastructure.Repositories;

/// <summary>Persistência de notificações no banco do tenant atual.</summary>
internal sealed class NotificationRepository(NotificationHubDbContext context) : INotificationRepository
{
	public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
	{
		context.Notifications.Add(notification);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task AddRangeAsync(
		IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(notifications);

		context.Notifications.AddRange(notifications);
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

	public async Task<PagedResult<Notification>> SearchAsync(
		NotificationSearchCriteria criteria,
		CancellationToken cancellationToken = default)
	{
		var query = context.Notifications.AsNoTracking();

		if (criteria.From is not null)
		{
			query = query.Where(notification => notification.CreatedAt >= criteria.From);
		}

		if (criteria.To is not null)
		{
			query = query.Where(notification => notification.CreatedAt <= criteria.To);
		}

		if (criteria.Status is not null)
		{
			query = query.Where(notification => notification.Status == criteria.Status);
		}

		if (criteria.Channel is not null)
		{
			query = query.Where(notification => notification.Channel == criteria.Channel);
		}

		if (criteria.Source is not null)
		{
			query = query.Where(notification => notification.Source == criteria.Source);
		}

		if (criteria.Type is not null)
		{
			query = query.Where(notification => notification.Type == criteria.Type);
		}

		if (criteria.ScheduledFrom is not null)
		{
			query = query.Where(notification => notification.ScheduledFor >= criteria.ScheduledFrom);
		}

		if (criteria.ScheduledTo is not null)
		{
			query = query.Where(notification => notification.ScheduledFor <= criteria.ScheduledTo);
		}

		var page = criteria.EffectivePage;
		var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

		var items = await query
			.OrderByDescending(notification => notification.CreatedAt)
			.ThenByDescending(notification => notification.Id)
			.Skip(page.Skip)
			.Take(page.Size)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return PagedResult.Create(items, page, totalCount);
	}
}
