using FluentAssertions;
using NSubstitute;
using Secco.NotificationHub.Infrastructure.Channels;
using Secco.NotificationHub.Infrastructure.Email;
using Secco.SDK.AspNetCore.BackgroundJobs;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.NotificationHub.Tests.Unit;

/// <summary>
/// Adaptadores de despacho (issue #24): sem <c>ScheduledFor</c> continuam enfileirando de
/// imediato como sempre fizeram; com um instante futuro, a chamada precisa virar agendamento
/// no <see cref="IBackgroundJobScheduler"/>, não enfileiramento imediato.
/// </summary>
public class DispatchSchedulingTests
{
	private sealed class FakeTenantContext(Guid? tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => tenantId is not null;
	}

	[Fact]
	public void EmailDispatchScheduler_WithoutScheduledFor_Enqueues()
	{
		var scheduler = Substitute.For<IBackgroundJobScheduler>();
		var tenantId = Guid.NewGuid();
		var adapter = new EmailDispatchScheduler(scheduler, new FakeTenantContext(tenantId));
		var notificationId = Guid.NewGuid();

		adapter.Enqueue(notificationId);

		scheduler.Received(1).Enqueue<SendEmailJob, SendEmailPayload>(
			tenantId, Arg.Is<SendEmailPayload>(payload => payload.NotificationId == notificationId));
		scheduler.DidNotReceiveWithAnyArgs().Schedule<SendEmailJob, SendEmailPayload>(default, default!, default);
	}

	[Fact]
	public void EmailDispatchScheduler_WithScheduledFor_Schedules()
	{
		var scheduler = Substitute.For<IBackgroundJobScheduler>();
		var tenantId = Guid.NewGuid();
		var adapter = new EmailDispatchScheduler(scheduler, new FakeTenantContext(tenantId));
		var notificationId = Guid.NewGuid();
		var scheduledFor = DateTimeOffset.UtcNow.AddDays(1);

		adapter.Enqueue(notificationId, scheduledFor);

		scheduler.Received(1).Schedule<SendEmailJob, SendEmailPayload>(
			tenantId, Arg.Is<SendEmailPayload>(payload => payload.NotificationId == notificationId), scheduledFor);
		scheduler.DidNotReceiveWithAnyArgs().Enqueue<SendEmailJob, SendEmailPayload>(default, default!);
	}

	[Fact]
	public void EmailDispatchScheduler_WithoutTenant_ThrowsDomainInvariantException()
	{
		var scheduler = Substitute.For<IBackgroundJobScheduler>();
		var adapter = new EmailDispatchScheduler(scheduler, new FakeTenantContext(null));

		var act = () => adapter.Enqueue(Guid.NewGuid());

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void ExternalChannelDispatchScheduler_WithoutScheduledFor_Enqueues()
	{
		var scheduler = Substitute.For<IBackgroundJobScheduler>();
		var tenantId = Guid.NewGuid();
		var adapter = new ExternalChannelDispatchScheduler(scheduler, new FakeTenantContext(tenantId));
		var notificationId = Guid.NewGuid();

		adapter.Enqueue(notificationId);

		scheduler.Received(1).Enqueue<SendExternalChannelJob, SendExternalChannelPayload>(
			tenantId, Arg.Is<SendExternalChannelPayload>(payload => payload.NotificationId == notificationId));
		scheduler.DidNotReceiveWithAnyArgs()
			.Schedule<SendExternalChannelJob, SendExternalChannelPayload>(default, default!, default);
	}

	[Fact]
	public void ExternalChannelDispatchScheduler_WithScheduledFor_Schedules()
	{
		var scheduler = Substitute.For<IBackgroundJobScheduler>();
		var tenantId = Guid.NewGuid();
		var adapter = new ExternalChannelDispatchScheduler(scheduler, new FakeTenantContext(tenantId));
		var notificationId = Guid.NewGuid();
		var scheduledFor = DateTimeOffset.UtcNow.AddHours(6);

		adapter.Enqueue(notificationId, scheduledFor);

		scheduler.Received(1).Schedule<SendExternalChannelJob, SendExternalChannelPayload>(
			tenantId, Arg.Is<SendExternalChannelPayload>(payload => payload.NotificationId == notificationId),
			scheduledFor);
		scheduler.DidNotReceiveWithAnyArgs()
			.Enqueue<SendExternalChannelJob, SendExternalChannelPayload>(default, default!);
	}
}
