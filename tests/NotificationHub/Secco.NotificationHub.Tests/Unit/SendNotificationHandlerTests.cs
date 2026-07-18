using FluentAssertions;
using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>Testes unitários do handler (ADR-0012): sem infraestrutura, fakes das portas.</summary>
public class SendNotificationHandlerTests
{
    private sealed class FakeRepository : INotificationRepository
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

    private static readonly NotificationHubOptions Options = new();

    [Fact]
    public async Task Handle_WithValidCommand_PersistsAsPendingAndEnqueuesDispatch()
    {
        var repository = new FakeRepository();
        var dispatchQueue = new FakeDispatchQueue();
        var handler = new SendNotificationHandler(repository, dispatchQueue, Options);

        var result = await handler.HandleAsync(new SendNotificationCommand("destinatario@teste.com", "Assunto", "Corpo"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(NotificationStatus.Pending);
        repository.Added.Should().ContainSingle().Which.Id.Should().Be(result.Value.Id);
        dispatchQueue.Enqueued.Should().ContainSingle().Which.Should().Be(result.Value.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WithoutRecipient_ReturnsValidationFailure(string? recipient)
    {
        var handler = new SendNotificationHandler(new FakeRepository(), new FakeDispatchQueue(), Options);

        var result = await handler.HandleAsync(new SendNotificationCommand(recipient, "Assunto", "Corpo"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.RecipientRequired);
    }

    [Theory]
    [InlineData("não é um e-mail")]
    [InlineData("sem-arroba.com")]
    public async Task Handle_WithInvalidRecipientFormat_ReturnsValidationFailure(string recipient)
    {
        var handler = new SendNotificationHandler(new FakeRepository(), new FakeDispatchQueue(), Options);

        var result = await handler.HandleAsync(new SendNotificationCommand(recipient, "Assunto", "Corpo"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.RecipientInvalid);
    }

    [Fact]
    public async Task Handle_WithSubjectAboveLimit_ReturnsValidationFailure()
    {
        var handler = new SendNotificationHandler(new FakeRepository(), new FakeDispatchQueue(), Options);

        var result = await handler.HandleAsync(new SendNotificationCommand(
            "destinatario@teste.com", new string('x', Options.MaxSubjectLength + 1), "Corpo"));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Handle_WithoutBody_ReturnsValidationFailure()
    {
        var handler = new SendNotificationHandler(new FakeRepository(), new FakeDispatchQueue(), Options);

        var result = await handler.HandleAsync(new SendNotificationCommand("destinatario@teste.com", "Assunto", ""));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.BodyRequired);
    }
}
