using FluentAssertions;
using NSubstitute;
using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>
/// Busca paginada de notificações (issue #23): descobrir quais entregas de uma publicação
/// falharam sem fazer N chamadas por identificador.
/// </summary>
public class SearchNotificationsHandlerTests
{
	private static readonly NotificationHubOptions Options = new();

	private static SearchNotificationsHandler CreateHandler(
		out INotificationRepository repository, NotificationHubOptions? options = null)
	{
		repository = Substitute.For<INotificationRepository>();
		return new SearchNotificationsHandler(repository, options ?? Options);
	}

	[Fact]
	public async Task HandleAsync_WithFromAfterTo_ReturnsInvalidDateRange()
	{
		var handler = CreateHandler(out _);
		var now = DateTimeOffset.UtcNow;

		var result = await handler.HandleAsync(new NotificationSearchCriteria(From: now, To: now.AddDays(-1)));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(NotificationHubErrors.Notifications.InvalidDateRange);
	}

	[Fact]
	public async Task HandleAsync_WithScheduledFromAfterScheduledTo_ReturnsInvalidDateRange()
	{
		var handler = CreateHandler(out _);
		var now = DateTimeOffset.UtcNow;

		var result = await handler.HandleAsync(
			new NotificationSearchCriteria(ScheduledFrom: now, ScheduledTo: now.AddDays(-1)));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(NotificationHubErrors.Notifications.InvalidDateRange);
	}

	[Fact]
	public async Task HandleAsync_WithSourceAboveLimit_ReturnsSourceTooLong()
	{
		var handler = CreateHandler(out _);

		var result = await handler.HandleAsync(
			new NotificationSearchCriteria(Source: new string('x', Options.MaxSourceLength + 1)));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(NotificationHubErrors.Notifications.SourceTooLong(Options.MaxSourceLength));
	}

	[Fact]
	public async Task HandleAsync_WithTypeAboveLimit_ReturnsTypeTooLong()
	{
		var handler = CreateHandler(out _);

		var result = await handler.HandleAsync(
			new NotificationSearchCriteria(Type: new string('x', Options.MaxTypeLength + 1)));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(NotificationHubErrors.Notifications.TypeTooLong(Options.MaxTypeLength));
	}

	[Fact]
	public async Task HandleAsync_WithMatches_ReturnsPagedDtosFromRepository()
	{
		var handler = CreateHandler(out var repository);

		var notification = new Notification(
			"destinatario@teste.com", "Publicação de setembro", "Corpo", "secco-intranet", "mural");
		var page = new PageRequest(1, 20);

		repository
			.SearchAsync(Arg.Any<NotificationSearchCriteria>(), Arg.Any<CancellationToken>())
			.Returns(PagedResult.Create([notification], page, totalCount: 1));

		var result = await handler.HandleAsync(
			new NotificationSearchCriteria(Source: "secco-intranet", Type: "mural"));

		result.IsSuccess.Should().BeTrue();
		result.Value.TotalCount.Should().Be(1);
		result.Value.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(NotificationDto.FromEntity(notification));
	}
}
