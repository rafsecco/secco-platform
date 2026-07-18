using FluentAssertions;
using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.NotificationHub.Domain.InAppNotifications;
using Xunit;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>Testes unitários dos handlers do inbox in-app (ADR-0012): sem infraestrutura, fake da porta.</summary>
public class InAppNotificationHandlersTests
{
    private sealed class FakeRepository : IInAppNotificationRepository
    {
        public List<InAppNotification> Items { get; } = [];

        public Task AddAsync(InAppNotification notification, CancellationToken cancellationToken = default)
        {
            Items.Add(notification);
            return Task.CompletedTask;
        }

        public Task<InAppNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(notification => notification.Id == id));

        public Task<IReadOnlyList<InAppNotification>> GetUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InAppNotification>>(
                Items.Where(notification => notification.UserId == userId && !notification.IsRead).ToList());

        public Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Count(notification => notification.UserId == userId && !notification.IsRead));

        public Task UpdateAsync(InAppNotification notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task GetUnread_ReturnsOnlyUnreadItemsOfTheGivenUser()
    {
        var repository = new FakeRepository();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var unreadA = new InAppNotification(userA, null, null, "Título", "Mensagem", null);
        var readA = new InAppNotification(userA, null, null, "Título", "Mensagem", null);
        readA.MarkAsRead();
        var unreadB = new InAppNotification(userB, null, null, "Título", "Mensagem", null);

        await repository.AddAsync(unreadA);
        await repository.AddAsync(readA);
        await repository.AddAsync(unreadB);

        var handler = new GetUnreadInAppNotificationsHandler(repository);
        var result = await handler.HandleAsync(userA);

        result.Should().ContainSingle().Which.Id.Should().Be(unreadA.Id);
    }

    [Fact]
    public async Task CountUnread_CountsOnlyUnreadItemsOfTheGivenUser()
    {
        var repository = new FakeRepository();
        var userId = Guid.NewGuid();

        await repository.AddAsync(new InAppNotification(userId, null, null, "Título", "Mensagem", null));
        await repository.AddAsync(new InAppNotification(userId, null, null, "Título", "Mensagem", null));

        var handler = new CountUnreadInAppNotificationsHandler(repository);
        var result = await handler.HandleAsync(userId);

        result.Should().Be(2);
    }

    [Fact]
    public async Task MarkAsRead_WithExistingItem_SetsIsReadAndReadAt()
    {
        var repository = new FakeRepository();
        var notification = new InAppNotification(Guid.NewGuid(), null, null, "Título", "Mensagem", null);
        await repository.AddAsync(notification);

        var handler = new MarkInAppNotificationAsReadHandler(repository);
        var result = await handler.HandleAsync(notification.Id);

        result.IsSuccess.Should().BeTrue();
        notification.IsRead.Should().BeTrue();
        notification.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkAsRead_WithUnknownId_ReturnsNotFound()
    {
        var handler = new MarkInAppNotificationAsReadHandler(new FakeRepository());

        var result = await handler.HandleAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(NotificationHubErrors.Notifications.InAppNotFound);
    }
}
