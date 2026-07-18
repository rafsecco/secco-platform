using FluentAssertions;
using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Domain.InAppNotifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>Testes unitários do handler (ADR-0012): sem infraestrutura, fakes das portas.</summary>
public class DispatchNotificationHandlerTests
{
    private sealed class FakeNotificationRepository : INotificationRepository
    {
        public List<Notification> Added { get; } = [];

        public Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
        {
            Added.Add(notification);
            return Task.CompletedTask;
        }

        public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.FirstOrDefault(notification => notification.Id == id));

        public Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeDispatchQueue : IEmailDispatchQueue
    {
        public List<Guid> Enqueued { get; } = [];

        public void Enqueue(Guid notificationId) => Enqueued.Add(notificationId);
    }

    private sealed class FakeInAppNotificationRepository : IInAppNotificationRepository
    {
        public List<InAppNotification> Added { get; } = [];

        public Task AddAsync(InAppNotification notification, CancellationToken cancellationToken = default)
        {
            Added.Add(notification);
            return Task.CompletedTask;
        }

        public Task<InAppNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.FirstOrDefault(notification => notification.Id == id));

        public Task<IReadOnlyList<InAppNotification>> GetUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InAppNotification>>(
                Added.Where(notification => notification.UserId == userId && !notification.IsRead).ToList());

        public Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.Count(notification => notification.UserId == userId && !notification.IsRead));

        public Task UpdateAsync(InAppNotification notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static readonly NotificationHubOptions Options = new();

    private static DispatchNotificationHandler CreateHandler(
        out FakeNotificationRepository notificationRepository,
        out FakeDispatchQueue dispatchQueue,
        out FakeInAppNotificationRepository inAppRepository)
    {
        notificationRepository = new FakeNotificationRepository();
        dispatchQueue = new FakeDispatchQueue();
        inAppRepository = new FakeInAppNotificationRepository();

        return new DispatchNotificationHandler(notificationRepository, dispatchQueue, inAppRepository, Options);
    }

    [Fact]
    public async Task Handle_WithEmailChannel_PersistsAsPendingAndEnqueuesDispatch()
    {
        var handler = CreateHandler(out var notifications, out var queue, out _);

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            null, "destinatario@teste.com", "Título", "Mensagem", null, null, null, [NotificationHubChannels.Email]));

        result.IsSuccess.Should().BeTrue();
        result.Value.EmailNotificationId.Should().NotBeNull();
        result.Value.InAppNotificationId.Should().BeNull();
        notifications.Added.Should().ContainSingle().Which.Status.Should().Be(NotificationStatus.Pending);
        queue.Enqueued.Should().ContainSingle().Which.Should().Be(result.Value.EmailNotificationId!.Value);
    }

    [Fact]
    public async Task Handle_WithInAppChannel_PersistsUnreadInboxItem()
    {
        var handler = CreateHandler(out _, out _, out var inApp);
        var userId = Guid.NewGuid();

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            userId, null, "Título", "Mensagem", "secco-intranet", "aviso", "/pagina", [NotificationHubChannels.InApp]));

        result.IsSuccess.Should().BeTrue();
        result.Value.InAppNotificationId.Should().NotBeNull();
        result.Value.EmailNotificationId.Should().BeNull();
        inApp.Added.Should().ContainSingle().Which.IsRead.Should().BeFalse();
        inApp.Added[0].UserId.Should().Be(userId);
    }

    [Fact]
    public async Task Handle_WithBothChannels_PersistsBothRecordsFromOneCall()
    {
        var handler = CreateHandler(out var notifications, out var queue, out var inApp);
        var userId = Guid.NewGuid();

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            userId, "destinatario@teste.com", "Título", "Mensagem", null, null, null,
            [NotificationHubChannels.Email, NotificationHubChannels.InApp]));

        result.IsSuccess.Should().BeTrue();
        notifications.Added.Should().ContainSingle();
        inApp.Added.Should().ContainSingle();
        queue.Enqueued.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WithoutChannels_ReturnsValidationFailure()
    {
        var handler = CreateHandler(out _, out _, out _);

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            null, "destinatario@teste.com", "Título", "Mensagem", null, null, null, []));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.ChannelsRequired);
    }

    [Fact]
    public async Task Handle_WithUnsupportedChannel_ReturnsValidationFailure()
    {
        var handler = CreateHandler(out _, out _, out _);

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            null, "destinatario@teste.com", "Título", "Mensagem", null, null, null, ["sms"]));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.ChannelUnsupported("sms"));
    }

    [Fact]
    public async Task Handle_WithEmailChannelAndNoRecipient_ReturnsValidationFailure()
    {
        var handler = CreateHandler(out _, out _, out _);

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            null, null, "Título", "Mensagem", null, null, null, [NotificationHubChannels.Email]));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.RecipientRequired);
    }

    [Fact]
    public async Task Handle_WithInAppChannelAndNoUserId_ReturnsValidationFailure()
    {
        var handler = CreateHandler(out _, out _, out _);

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            null, null, "Título", "Mensagem", null, null, null, [NotificationHubChannels.InApp]));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.UserIdRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WithoutTitle_ReturnsValidationFailure(string? title)
    {
        var handler = CreateHandler(out _, out _, out _);

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            null, "destinatario@teste.com", title, "Mensagem", null, null, null, [NotificationHubChannels.Email]));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.TitleRequired);
    }

    [Fact]
    public async Task Handle_WithoutMessage_ReturnsValidationFailure()
    {
        var handler = CreateHandler(out _, out _, out _);

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            null, "destinatario@teste.com", "Título", "", null, null, null, [NotificationHubChannels.Email]));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.MessageRequired);
    }

    [Fact]
    public async Task Handle_WithInvalidRecipientFormat_ReturnsValidationFailure()
    {
        var handler = CreateHandler(out _, out _, out _);

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            null, "não é um e-mail", "Título", "Mensagem", null, null, null, [NotificationHubChannels.Email]));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.RecipientInvalid);
    }

    [Fact]
    public async Task Handle_WithTitleAboveLimit_ReturnsValidationFailure()
    {
        var handler = CreateHandler(out _, out _, out _);

        var result = await handler.HandleAsync(new DispatchNotificationCommand(
            null, "destinatario@teste.com", new string('x', Options.MaxTitleLength + 1), "Mensagem",
            null, null, null, [NotificationHubChannels.Email]));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }
}
