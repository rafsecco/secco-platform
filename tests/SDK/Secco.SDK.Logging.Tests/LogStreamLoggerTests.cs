using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Secco.LogStream.Client;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.Logging.Internal;
using Xunit;

namespace Secco.SDK.Logging.Tests;

/// <summary>
/// O caminho de escrita do sink: o que entra na fila, o que é ignorado e o que é descartado.
/// </summary>
public class LogStreamLoggerTests
{
	/// <summary>
	/// Estabelece o tenant ambiente pelo caminho público do SDK — o mesmo que um job usa
	/// (ADR-0015). Testar pelo caminho real vale mais que escrever o <c>AsyncLocal</c> à mão.
	/// </summary>
	private static IDisposable AmbientTenant(Guid tenantId)
	{
		var services = new ServiceCollection();
		services.AddSeccoTenancy();

		var provider = services.BuildServiceProvider();
		var scope = provider.CreateScope();

		scope.ServiceProvider.SetTenant(tenantId);

		return new CompositeDisposable(scope, provider);
	}

	private static (LogStreamLogQueue Queue, ILoggerProvider Provider) CreateSink(
		Action<LogStreamLoggerOptions>? configure = null)
	{
		var options = new LogStreamLoggerOptions();
		configure?.Invoke(options);

		var queue = new LogStreamLogQueue(options.QueueCapacity);

		return (queue, new LogStreamLoggerProvider(queue, Options.Create(options)));
	}

	[Fact]
	public void Log_WhenCategoryIsHttpClient_IsIgnored()
	{
		// A guarda anti-recursão: o próprio envio do lote loga nesta categoria.
		var (queue, provider) = CreateSink();
		using var tenant = AmbientTenant(Guid.CreateVersion7());

		var logger = provider.CreateLogger("System.Net.Http.HttpClient.Secco.LogStream.LogicalHandler");
		logger.LogError("qualquer coisa");

		logger.Should().BeSameAs(NullLogger.Instance);
		queue.Reader.TryRead(out _).Should().BeFalse();
	}

	[Fact]
	public void Log_WhenCategoryIsTheSinkItself_IsIgnored()
	{
		var (queue, provider) = CreateSink();
		using var tenant = AmbientTenant(Guid.CreateVersion7());

		provider.CreateLogger("Secco.SDK.Logging.Internal.LogStreamDispatcher").LogError("descarte");

		queue.Reader.TryRead(out _).Should().BeFalse();
	}

	[Fact]
	public void Log_WhenBelowMinimumLevel_IsIgnored()
	{
		var (queue, provider) = CreateSink(options => options.MinimumLevel = LogLevel.Warning);
		using var tenant = AmbientTenant(Guid.CreateVersion7());

		var logger = provider.CreateLogger("Secco.Intranet.Documentos");
		logger.LogInformation("abaixo do mínimo");

		queue.Reader.TryRead(out _).Should().BeFalse();

		logger.LogWarning("no mínimo");

		queue.Reader.TryRead(out _).Should().BeTrue();
	}

	[Fact]
	public void Log_WhenQueueIsFull_DropsAndCounts()
	{
		var (queue, provider) = CreateSink(options => options.QueueCapacity = 2);
		using var tenant = AmbientTenant(Guid.CreateVersion7());

		var logger = provider.CreateLogger("Secco.Intranet.Documentos");

		for (var i = 0; i < 5; i++)
		{
			logger.LogError("mensagem {Index}", i);
		}

		queue.DroppedCount.Should().Be(3);
	}

	[Fact]
	public void Log_WhenNoTenantAndNoPlatformTenant_IsDropped()
	{
		// Sem tenant não há banco de destino (ADR-0005): a entrada morre antes da fila.
		var (queue, provider) = CreateSink();

		provider.CreateLogger("Secco.Intranet.Startup").LogError("falha no startup");

		queue.Reader.TryRead(out _).Should().BeFalse();
	}

	[Fact]
	public void Log_WhenNoTenantAndPlatformTenantConfigured_UsesPlatformTenant()
	{
		var platformTenantId = Guid.CreateVersion7();
		var (queue, provider) = CreateSink(options => options.PlatformTenantId = platformTenantId);

		provider.CreateLogger("Secco.Intranet.Startup").LogError("falha no startup");

		queue.Reader.TryRead(out var entry).Should().BeTrue();
		entry!.TenantId.Should().Be(platformTenantId);
	}

	[Fact]
	public void Log_WhenTenantResolved_PrefersItOverPlatformTenant()
	{
		var tenantId = Guid.CreateVersion7();
		var (queue, provider) = CreateSink(options => options.PlatformTenantId = Guid.CreateVersion7());
		using var tenant = AmbientTenant(tenantId);

		provider.CreateLogger("Secco.Intranet.Documentos").LogError("erro do tenant");

		queue.Reader.TryRead(out var entry).Should().BeTrue();
		entry!.TenantId.Should().Be(tenantId);
	}

	[Fact]
	public void Log_WhenMessageExceedsLimit_IsTruncated()
	{
		var (queue, provider) = CreateSink(options => options.MaxMessageLength = 32);
		using var tenant = AmbientTenant(Guid.CreateVersion7());

		provider.CreateLogger("Secco.Intranet.Documentos").LogError(new string('x', 500));

		queue.Reader.TryRead(out var entry).Should().BeTrue();
		entry!.Message.Should().HaveLength(32 + "… [truncado]".Length);
		entry.Message.Should().EndWith("[truncado]");
	}

	[Fact]
	public void Log_WithException_CapturesStackTraceAndCategoryAndService()
	{
		var (queue, provider) = CreateSink(options => options.ServiceName = "secco-intranet");
		using var tenant = AmbientTenant(Guid.CreateVersion7());

		provider.CreateLogger("Secco.Intranet.Documentos.UploadHandler")
			.LogError(new InvalidOperationException("falhou"), "upload falhou");

		queue.Reader.TryRead(out var entry).Should().BeTrue();
		entry!.Category.Should().Be("Secco.Intranet.Documentos.UploadHandler");
		entry.ServiceName.Should().Be("secco-intranet");
		entry.StackTrace.Should().Contain(nameof(InvalidOperationException));
	}

	[Fact]
	public void Log_WhenSinkIsDisabled_ProducesNoEntries()
	{
		var (queue, provider) = CreateSink(options => options.Enabled = false);
		using var tenant = AmbientTenant(Guid.CreateVersion7());

		provider.CreateLogger("Secco.Intranet.Documentos").LogError("erro");

		queue.Reader.TryRead(out _).Should().BeFalse();
	}

	[Theory]
	[InlineData(LogLevel.Trace, LogEntryLevel.Trace)]
	[InlineData(LogLevel.Debug, LogEntryLevel.Debug)]
	[InlineData(LogLevel.Information, LogEntryLevel.Information)]
	[InlineData(LogLevel.Warning, LogEntryLevel.Warning)]
	[InlineData(LogLevel.Error, LogEntryLevel.Error)]
	[InlineData(LogLevel.Critical, LogEntryLevel.Critical)]
	public void Map_LogLevel_To_LogEntryLevel(LogLevel level, LogEntryLevel expected) =>
		LogStreamIngestionGateway.ToLogEntryLevel(level).Should().Be(expected);

	private sealed class CompositeDisposable(params IDisposable[] disposables) : IDisposable
	{
		public void Dispose()
		{
			foreach (var disposable in disposables.Reverse())
			{
				disposable.Dispose();
			}
		}
	}
}
