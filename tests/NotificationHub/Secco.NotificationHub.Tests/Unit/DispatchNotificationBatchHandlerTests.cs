using FluentAssertions;
using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Domain.InAppNotifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>
/// Despacho em lote (issue #15): um conteúdo para muitos destinos, validação tudo-ou-nada e
/// uma única ida ao banco — que é o que faz o lote valer mais que N chamadas.
/// </summary>
public class DispatchNotificationBatchHandlerTests
{
	private sealed class CountingNotificationRepository : INotificationRepository
	{
		public List<Notification> Added { get; } = [];

		public int SaveCount { get; private set; }

		public Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
		{
			Added.Add(notification);
			SaveCount++;
			return Task.CompletedTask;
		}

		public Task AddRangeAsync(
			IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken = default)
		{
			Added.AddRange(notifications);
			SaveCount++;
			return Task.CompletedTask;
		}

		public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(Added.FirstOrDefault(notification => notification.Id == id));

		public Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task<PagedResult<Notification>> SearchAsync(
			NotificationSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Não exercitado pelos testes de despacho em lote.");
	}

	private sealed class CountingInAppRepository : IInAppNotificationRepository
	{
		public List<InAppNotification> Added { get; } = [];

		public int SaveCount { get; private set; }

		public Task AddAsync(InAppNotification notification, CancellationToken cancellationToken = default)
		{
			Added.Add(notification);
			SaveCount++;
			return Task.CompletedTask;
		}

		public Task AddRangeAsync(
			IReadOnlyCollection<InAppNotification> notifications, CancellationToken cancellationToken = default)
		{
			Added.AddRange(notifications);
			SaveCount++;
			return Task.CompletedTask;
		}

		public Task<InAppNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(Added.FirstOrDefault(item => item.Id == id));

		public Task<IReadOnlyList<InAppNotification>> GetUnreadByUserAsync(
			Guid userId, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<InAppNotification>>([]);

		public Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
			Task.FromResult(0);

		public Task UpdateAsync(InAppNotification notification, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class FakeDispatchQueue : IEmailDispatchQueue
	{
		public List<Guid> Enqueued { get; } = [];

		public void Enqueue(Guid notificationId) => Enqueued.Add(notificationId);
	}

	private static DispatchNotificationBatchCommand Command(
		IReadOnlyList<NotificationDestination> destinations, params string[] channels) =>
		new("Aviso do Mural", "Corpo da publicacao", "mural", "publicacao", null,
			channels.Length > 0 ? channels : [NotificationHubChannels.Email], destinations);

	private static (DispatchNotificationBatchHandler Handler,
		CountingNotificationRepository Notifications,
		CountingInAppRepository InApp,
		FakeDispatchQueue Queue) Create(NotificationHubOptions? options = null) =>
		CreateWithChannels(out _, out _, options);

	private static (DispatchNotificationBatchHandler Handler,
		CountingNotificationRepository Notifications,
		CountingInAppRepository InApp,
		FakeDispatchQueue Queue) CreateWithChannels(
			out FakeChannelConfigurationRepository channelConfigurations,
			out FakeExternalChannelDispatchQueue externalQueue,
			NotificationHubOptions? options = null)
	{
		var notifications = new CountingNotificationRepository();
		var inApp = new CountingInAppRepository();
		var queue = new FakeDispatchQueue();
		channelConfigurations = new FakeChannelConfigurationRepository();
		externalQueue = new FakeExternalChannelDispatchQueue();

		return (
			new DispatchNotificationBatchHandler(
				notifications, queue, inApp, channelConfigurations, externalQueue,
				options ?? new NotificationHubOptions()),
			notifications, inApp, queue);
	}

	[Fact]
	public async Task Batch_WithManyDestinations_PersistsInASingleRoundTrip()
	{
		// O ponto da issue #15: um lote em laço sobre AddAsync trocaria N chamadas HTTP por N
		// idas ao banco. Uma gravação só é o que torna o endpoint melhor que o laço do chamador.
		var (handler, notifications, _, queue) = Create();

		var destinations = Enumerable.Range(0, 50)
			.Select(index => new NotificationDestination(null, $"pessoa{index}@empresa.com"))
			.ToList();

		var result = await handler.HandleAsync(Command(destinations));

		result.IsSuccess.Should().BeTrue();
		notifications.Added.Should().HaveCount(50);
		notifications.SaveCount.Should().Be(1);
		queue.Enqueued.Should().HaveCount(50, "o retry continua sendo por notificação (ADR-0015)");
	}

	[Fact]
	public async Task Batch_WithSourceAndType_PersistsThemOnEveryNotification()
	{
		// Defeito corrigido pela issue #23: Source/Type eram validados e depois descartados —
		// só chegavam ao InAppNotification, nunca à Notification de e-mail.
		var (handler, notifications, _, _) = Create();

		var destinations = new List<NotificationDestination> { new(null, "pessoa@empresa.com") };

		var result = await handler.HandleAsync(Command(destinations));

		result.IsSuccess.Should().BeTrue();
		var persisted = notifications.Added.Should().ContainSingle().Subject;
		persisted.Source.Should().Be("mural");
		persisted.Type.Should().Be("publicacao");
	}

	[Fact]
	public async Task Batch_ReturnsOneResultPerDestinationInOrder()
	{
		var (handler, notifications, _, _) = Create();

		var destinations = Enumerable.Range(0, 3)
			.Select(index => new NotificationDestination(null, $"pessoa{index}@empresa.com"))
			.ToList();

		var result = await handler.HandleAsync(Command(destinations));

		result.Value.Results.Should().HaveCount(3);
		result.Value.Results.Select(item => item.EmailNotificationId)
			.Should().BeEquivalentTo(notifications.Added.Select(item => (Guid?)item.Id), o => o.WithStrictOrdering());
	}

	[Fact]
	public async Task Batch_WhenOneDestinationIsInvalid_WritesNothingAndPointsToThePosition()
	{
		// Tudo-ou-nada: gravar os válidos e reportar o resto deixaria o chamador sem saber o
		// que repetir — a dor que a issue descreve.
		var (handler, notifications, _, queue) = Create();

		var destinations = new List<NotificationDestination>
		{
			new(null, "ok@empresa.com"),
			new(null, "isto-nao-e-email"),
			new(null, "tambem-ok@empresa.com"),
		};

		var result = await handler.HandleAsync(Command(destinations));

		result.IsFailure.Should().BeTrue();
		result.Error.Description.Should().Contain("posição 1");
		notifications.Added.Should().BeEmpty();
		queue.Enqueued.Should().BeEmpty();
	}

	[Fact]
	public async Task Batch_WhenAboveTheConfiguredLimit_IsRefused()
	{
		var (handler, _, _, _) = Create(new NotificationHubOptions { MaxBatchDestinations = 2 });

		var destinations = Enumerable.Range(0, 3)
			.Select(index => new NotificationDestination(null, $"pessoa{index}@empresa.com"))
			.ToList();

		var result = await handler.HandleAsync(Command(destinations));

		result.Error.Should().Be(NotificationHubErrors.Notifications.TooManyDestinations(2));
	}

	[Fact]
	public async Task Batch_WithoutDestinations_IsRefused()
	{
		var (handler, _, _, _) = Create();

		var result = await handler.HandleAsync(Command([]));

		result.Error.Should().Be(NotificationHubErrors.Notifications.DestinationsRequired);
	}

	[Fact]
	public async Task Batch_WithBothChannels_CreatesBothPerDestination()
	{
		var (handler, notifications, inApp, _) = Create();

		var destinations = new List<NotificationDestination>
		{
			new(Guid.CreateVersion7(), "um@empresa.com"),
			new(Guid.CreateVersion7(), "dois@empresa.com"),
		};

		var result = await handler.HandleAsync(
			Command(destinations, NotificationHubChannels.Email, NotificationHubChannels.InApp));

		result.IsSuccess.Should().BeTrue();
		notifications.Added.Should().HaveCount(2);
		inApp.Added.Should().HaveCount(2);
		inApp.SaveCount.Should().Be(1);
	}

	[Fact]
	public async Task Batch_WhenContentIsInvalid_IsRefusedBeforeLookingAtDestinations()
	{
		var (handler, notifications, _, _) = Create();

		var result = await handler.HandleAsync(
			new DispatchNotificationBatchCommand(
				Title: null, Message: "Corpo", Source: null, Type: null, Link: null,
				Channels: [NotificationHubChannels.Email],
				Destinations: [new NotificationDestination(null, "ok@empresa.com")]));

		result.Error.Should().Be(NotificationHubErrors.Notifications.TitleRequired);
		notifications.Added.Should().BeEmpty();
	}
}
