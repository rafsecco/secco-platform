using Secco.NotificationHub.Application.Channels;
using Secco.NotificationHub.Domain.Channels;
using Secco.SharedKernel.Pagination;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>Configuração de canal em memória, compartilhada pelos testes de despacho.</summary>
internal sealed class FakeChannelConfigurationRepository : IChannelConfigurationRepository
{
	private readonly Dictionary<string, ChannelConfiguration> _store = new(StringComparer.Ordinal);

	/// <summary>Semeia um canal já configurado e ativo.</summary>
	/// <param name="channel">Canal (minúsculo).</param>
	/// <param name="destination">Destino de teste.</param>
	/// <param name="enabled">Se fica ativo.</param>
	public void Seed(string channel, string destination = "https://exemplo.com/webhook", bool enabled = true) =>
		_store[channel] = new ChannelConfiguration(channel, destination, enabled);

	public Task<ChannelConfiguration?> GetAsync(string channel, CancellationToken cancellationToken = default) =>
		Task.FromResult(_store.GetValueOrDefault(channel));

	public Task<IReadOnlyList<ChannelConfiguration>> ListAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<ChannelConfiguration>>([.. _store.Values]);

	public Task AddAsync(ChannelConfiguration configuration, CancellationToken cancellationToken = default)
	{
		_store[configuration.Channel] = configuration;
		return Task.CompletedTask;
	}

	public Task RemoveAsync(ChannelConfiguration configuration, CancellationToken cancellationToken = default)
	{
		_store.Remove(configuration.Channel);
		return Task.CompletedTask;
	}

	public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Fila de despacho externo em memória.</summary>
internal sealed class FakeExternalChannelDispatchQueue : IExternalChannelDispatchQueue
{
	public List<Guid> Enqueued { get; } = [];

	public void Enqueue(Guid notificationId) => Enqueued.Add(notificationId);
}

/// <summary>Repositório de notificações em memória, para os testes de canal externo.</summary>
internal sealed class FakeNotificationRepositoryForChannels : Secco.NotificationHub.Application.Notifications.INotificationRepository
{
	public List<Secco.NotificationHub.Domain.Notifications.Notification> Added { get; } = [];

	public Task AddAsync(Secco.NotificationHub.Domain.Notifications.Notification notification, CancellationToken cancellationToken = default)
	{
		Added.Add(notification);
		return Task.CompletedTask;
	}

	public Task AddRangeAsync(IReadOnlyCollection<Secco.NotificationHub.Domain.Notifications.Notification> notifications, CancellationToken cancellationToken = default)
	{
		Added.AddRange(notifications);
		return Task.CompletedTask;
	}

	public Task<Secco.NotificationHub.Domain.Notifications.Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		Task.FromResult(Added.FirstOrDefault(notification => notification.Id == id));

	public Task UpdateAsync(Secco.NotificationHub.Domain.Notifications.Notification notification, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	public Task<PagedResult<Secco.NotificationHub.Domain.Notifications.Notification>> SearchAsync(
		Secco.NotificationHub.Application.Notifications.NotificationSearchCriteria criteria, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException("Não exercitado pelos testes de canal externo.");
}

/// <summary>Fila de e-mail em memória, para os testes de canal externo.</summary>
internal sealed class FakeEmailDispatchQueue : Secco.NotificationHub.Application.Notifications.IEmailDispatchQueue
{
	public List<Guid> Enqueued { get; } = [];

	public void Enqueue(Guid notificationId) => Enqueued.Add(notificationId);
}

/// <summary>Inbox in-app em memória, para os testes de canal externo.</summary>
internal sealed class FakeInAppRepositoryForChannels : Secco.NotificationHub.Application.InAppNotifications.IInAppNotificationRepository
{
	public List<Secco.NotificationHub.Domain.InAppNotifications.InAppNotification> Added { get; } = [];

	public Task AddAsync(Secco.NotificationHub.Domain.InAppNotifications.InAppNotification notification, CancellationToken cancellationToken = default)
	{
		Added.Add(notification);
		return Task.CompletedTask;
	}

	public Task AddRangeAsync(IReadOnlyCollection<Secco.NotificationHub.Domain.InAppNotifications.InAppNotification> notifications, CancellationToken cancellationToken = default)
	{
		Added.AddRange(notifications);
		return Task.CompletedTask;
	}

	public Task<Secco.NotificationHub.Domain.InAppNotifications.InAppNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		Task.FromResult(Added.FirstOrDefault(item => item.Id == id));

	public Task<IReadOnlyList<Secco.NotificationHub.Domain.InAppNotifications.InAppNotification>> GetUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<Secco.NotificationHub.Domain.InAppNotifications.InAppNotification>>([]);

	public Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(0);

	public Task UpdateAsync(Secco.NotificationHub.Domain.InAppNotifications.InAppNotification notification, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;
}
