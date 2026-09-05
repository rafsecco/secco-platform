using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Secco.SDK.Logging.Internal;
using Xunit;

namespace Secco.SDK.Logging.Tests;

/// <summary>
/// O caminho de envio: agrupamento por tenant, tolerância a falha e flush no encerramento.
/// </summary>
public class LogStreamDispatcherTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	private static PendingLogEntry Entry(Guid tenantId, string message = "mensagem") =>
		new(tenantId, Guid.CreateVersion7(), LogLevel.Error, message, null, "secco-intranet", "Secco.Intranet");

	private static (LogStreamDispatcher Dispatcher, LogStreamLogQueue Queue, ILogStreamIngestionGateway Gateway)
		CreateDispatcher(Action<LogStreamLoggerOptions>? configure = null)
	{
		var options = new LogStreamLoggerOptions { FlushIntervalMs = 50 };
		configure?.Invoke(options);

		var queue = new LogStreamLogQueue(options.QueueCapacity);
		var gateway = Substitute.For<ILogStreamIngestionGateway>();

		return (
			new LogStreamDispatcher(queue, gateway, Options.Create(options), NullLogger<LogStreamDispatcher>.Instance),
			queue,
			gateway);
	}

	/// <summary>Espera a condição por até <see cref="Timeout"/>, sem depender de tempo fixo.</summary>
	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < Timeout)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(20);
		}

		throw new TimeoutException("A condição esperada não ocorreu dentro do tempo limite.");
	}

	[Fact]
	public async Task Dispatch_WhenBatchHasMultipleTenants_SendsOneCallPerTenant()
	{
		// Isolamento de tenant (ADR-0005): uma entrada jamais viaja no lote de outro tenant.
		var (dispatcher, queue, gateway) = CreateDispatcher();

		var first = Guid.CreateVersion7();
		var second = Guid.CreateVersion7();

		queue.Enqueue(Entry(first, "a"));
		queue.Enqueue(Entry(second, "b"));
		queue.Enqueue(Entry(first, "c"));

		await dispatcher.StartAsync(CancellationToken.None);
		await WaitUntilAsync(() => gateway.ReceivedCalls().Count() >= 2);
		await dispatcher.StopAsync(CancellationToken.None);

		await gateway.Received(1).SendAsync(
			first,
			Arg.Is<IReadOnlyList<PendingLogEntry>>(entries => entries.Count == 2),
			Arg.Any<CancellationToken>());

		await gateway.Received(1).SendAsync(
			second,
			Arg.Is<IReadOnlyList<PendingLogEntry>>(entries => entries.Count == 1),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Dispatch_WhenGatewayThrows_DiscardsBatchAndDoesNotRethrow()
	{
		// ADR-0008: LogStream indisponível não pode derrubar o produto.
		var (dispatcher, queue, gateway) = CreateDispatcher();

		gateway
			.SendAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<PendingLogEntry>>(), Arg.Any<CancellationToken>())
			.ThrowsAsync(new HttpRequestException("LogStream fora do ar"));

		queue.Enqueue(Entry(Guid.CreateVersion7()));

		await dispatcher.StartAsync(CancellationToken.None);
		await WaitUntilAsync(() => gateway.ReceivedCalls().Any());

		var stop = async () => await dispatcher.StopAsync(CancellationToken.None);

		await stop.Should().NotThrowAsync();
	}

	[Fact]
	public async Task Dispatch_WhenBatchSizeIsReached_SendsWithoutWaitingForTheFlushWindow()
	{
		var (dispatcher, queue, gateway) = CreateDispatcher(options =>
		{
			options.BatchSize = 3;
			options.FlushIntervalMs = 60_000;
		});

		var tenantId = Guid.CreateVersion7();

		for (var i = 0; i < 3; i++)
		{
			queue.Enqueue(Entry(tenantId, $"mensagem {i}"));
		}

		await dispatcher.StartAsync(CancellationToken.None);
		await WaitUntilAsync(() => gateway.ReceivedCalls().Any());
		await dispatcher.StopAsync(CancellationToken.None);

		await gateway.Received(1).SendAsync(
			tenantId,
			Arg.Is<IReadOnlyList<PendingLogEntry>>(entries => entries.Count == 3),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Stop_WhenQueueStillHasEntries_FlushesThemBeforeReturning()
	{
		// Sem o flush de encerramento, o último lote de cada restart se perderia.
		var (dispatcher, queue, gateway) = CreateDispatcher(options =>
		{
			options.BatchSize = 500;
			options.FlushIntervalMs = 60_000;
		});

		await dispatcher.StartAsync(CancellationToken.None);

		var tenantId = Guid.CreateVersion7();
		queue.Enqueue(Entry(tenantId, "pendente"));

		await dispatcher.StopAsync(CancellationToken.None);

		await gateway.Received().SendAsync(
			tenantId,
			Arg.Is<IReadOnlyList<PendingLogEntry>>(entries => entries.Count == 1),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Dispatch_WhenSinkIsDisabled_NeverCallsTheGateway()
	{
		var (dispatcher, queue, gateway) = CreateDispatcher(options => options.Enabled = false);

		queue.Enqueue(Entry(Guid.CreateVersion7()));

		await dispatcher.StartAsync(CancellationToken.None);
		await Task.Delay(200);
		await dispatcher.StopAsync(CancellationToken.None);

		gateway.ReceivedCalls().Should().BeEmpty();
	}
}
