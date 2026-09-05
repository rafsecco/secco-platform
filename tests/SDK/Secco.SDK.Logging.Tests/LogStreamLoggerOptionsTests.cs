using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Secco.SDK.Logging.Tests;

public class LogStreamLoggerOptionsTests
{
	[Fact]
	public void SectionKey_IsSeccoLogStream()
	{
		LogStreamLoggerOptions.SectionKey.Should().Be("Secco:LogStream");
	}

	[Fact]
	public void Defaults_MatchDesign()
	{
		var options = new LogStreamLoggerOptions();

		options.Enabled.Should().BeTrue();
		options.BaseUrl.Should().BeNull();
		options.ClientId.Should().BeNull();
		options.ClientSecret.Should().BeNull();
		options.Scope.Should().Be("logstream");
		options.ServiceName.Should().BeNull();
		options.PlatformTenantId.Should().BeNull();
		options.MinimumLevel.Should().Be(LogLevel.Information);
		options.QueueCapacity.Should().Be(10_000);
		options.BatchSize.Should().Be(100);
		options.FlushIntervalMs.Should().Be(2_000);
		options.ShutdownFlushTimeoutMs.Should().Be(5_000);
		options.MaxMessageLength.Should().Be(8_000);
		options.MaxStackTraceLength.Should().Be(16_000);
	}
}
