using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Secco.SDK.AspNetCore.BackgroundJobs;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.BackgroundJobs;

/// <summary>
/// <see cref="HangfireBackgroundJobScheduler"/> só traduz chamadas para o client do Hangfire
/// (ADR-0015 Camada 2) — este teste prova que <see cref="IBackgroundJobScheduler.Schedule"/>
/// chega ao Hangfire como agendamento (<see cref="ScheduledState"/>) no instante pedido, e não
/// como enfileiramento imediato.
/// </summary>
public class HangfireBackgroundJobSchedulerTests
{
	private sealed record FakePayload(string Value);

	private sealed class FakeJob : IBackgroundJob<FakePayload>
	{
		public Task ExecuteAsync(FakePayload payload, CancellationToken cancellationToken) => Task.CompletedTask;
	}

	/// <summary>
	/// Dublê de <see cref="IBackgroundJobClient"/> — interface do Hangfire com só dois métodos,
	/// simples o bastante para não precisar de biblioteca de mock (mesmo padrão dos outros
	/// testes de background job deste projeto).
	/// </summary>
	private sealed class FakeBackgroundJobClient : IBackgroundJobClient
	{
		public Job? CapturedJob { get; private set; }

		public IState? CapturedState { get; private set; }

		public string Create(Job job, IState state)
		{
			CapturedJob = job;
			CapturedState = state;
			return Guid.NewGuid().ToString();
		}

		public bool ChangeState(string jobId, IState state, string? expectedState) =>
			throw new NotSupportedException("Não exercitado por este teste.");
	}

	[Fact]
	public void Schedule_DelegatesToHangfireWithTheRequestedInstant()
	{
		var client = new FakeBackgroundJobClient();
		var scheduler = new HangfireBackgroundJobScheduler(client);
		var tenantId = Guid.NewGuid();
		var payload = new FakePayload("conteúdo");
		var enqueueAt = DateTimeOffset.UtcNow.AddHours(3);

		var jobId = scheduler.Schedule<FakeJob, FakePayload>(tenantId, payload, enqueueAt);

		jobId.Should().NotBeNullOrWhiteSpace();

		client.CapturedJob.Should().NotBeNull();
		client.CapturedJob!.Type.Should().Be<TenantJobRunner<FakeJob, FakePayload>>();
		client.CapturedJob.Method.Name.Should().Be(nameof(TenantJobRunner<FakeJob, FakePayload>.RunAsync));
		client.CapturedJob.Args.Should().Equal(tenantId, payload, CancellationToken.None);

		client.CapturedState.Should().BeOfType<ScheduledState>();
		((ScheduledState)client.CapturedState!).EnqueueAt.Should().Be(enqueueAt.UtcDateTime);
	}

	[Fact]
	public void Enqueue_StillUsesImmediateEnqueuedState()
	{
		// Garante que o Schedule novo não regrediu o Enqueue existente para agendamento.
		var client = new FakeBackgroundJobClient();
		var scheduler = new HangfireBackgroundJobScheduler(client);

		scheduler.Enqueue<FakeJob, FakePayload>(Guid.NewGuid(), new FakePayload("conteúdo"));

		client.CapturedState.Should().BeOfType<EnqueuedState>();
	}
}
