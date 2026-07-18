using FluentAssertions;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.NotificationHub.Infrastructure.Email;
using Xunit;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Testa <see cref="SendEmailJob"/> isolado (fakes das portas) — mais rápido e
/// determinístico que esperar o Hangfire real processar a fila (coberto em
/// <see cref="NotificationEndpointsTests"/>); aqui também cobre o branch de
/// notificação inexistente, que o E2E não exercita.
/// </summary>
public class SendEmailJobTests
{
    private sealed class FakeRepository : INotificationRepository
    {
        private readonly Dictionary<Guid, Notification> _store = [];

        public Notification? Updated { get; private set; }

        public void Seed(Notification notification) => _store[notification.Id] = notification;

        public Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
        {
            _store[notification.Id] = notification;
            return Task.CompletedTask;
        }

        public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.GetValueOrDefault(id));

        public Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default)
        {
            Updated = notification;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSender(Exception? failWith = null) : IEmailSender
    {
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken) =>
            failWith is null ? Task.CompletedTask : throw failWith;
    }

    [Fact]
    public async Task ExecuteAsync_WhenSendSucceeds_MarksNotificationAsSent()
    {
        var repository = new FakeRepository();
        var notification = new Notification("destinatario@teste.com", "Assunto", "Corpo");
        repository.Seed(notification);

        var job = new SendEmailJob(repository, new FakeSender());

        await job.ExecuteAsync(new SendEmailPayload(notification.Id), CancellationToken.None);

        repository.Updated!.Status.Should().Be(NotificationStatus.Sent);
        repository.Updated.SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenSendFails_MarksAsFailedAndRethrows()
    {
        var repository = new FakeRepository();
        var notification = new Notification("destinatario@teste.com", "Assunto", "Corpo");
        repository.Seed(notification);

        var job = new SendEmailJob(repository, new FakeSender(new InvalidOperationException("SMTP fora do ar")));

        var act = async () => await job.ExecuteAsync(new SendEmailPayload(notification.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "quem decide o retry é o Hangfire — o job relança a exceção");
        repository.Updated!.Status.Should().Be(NotificationStatus.Failed);
        repository.Updated.FailureReason.Should().Contain("SMTP fora do ar");
    }

    [Fact]
    public async Task ExecuteAsync_WhenNotificationDoesNotExist_DoesNothing()
    {
        var repository = new FakeRepository();
        var job = new SendEmailJob(repository, new FakeSender());

        await job.ExecuteAsync(new SendEmailPayload(Guid.NewGuid()), CancellationToken.None);

        repository.Updated.Should().BeNull();
    }
}
