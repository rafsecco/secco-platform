using System.Net;
using FluentAssertions;
using Secco.SDK.AspNetCore.HealthChecks;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.HealthChecks;

public class SeccoHealthCheckCommandTests
{
	private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
		: HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> respond(request, cancellationToken);
	}

	private static FakeHandler Responding(HttpStatusCode status) => new((request, _) =>
	{
		request.RequestUri.Should().Be(new Uri("http://127.0.0.1:8080/health/ready"));
		request.Headers.Host.Should().Be("localhost");
		return Task.FromResult(new HttpResponseMessage(status));
	});

	[Fact]
	public void IsHealthCheck_PrimeiroArgumentoExato_True()
	{
		SeccoHealthCheckCommand.IsHealthCheck(["healthcheck"]).Should().BeTrue();
		SeccoHealthCheckCommand.IsHealthCheck(["HealthCheck"]).Should().BeFalse();
		SeccoHealthCheckCommand.IsHealthCheck([]).Should().BeFalse();
		SeccoHealthCheckCommand.IsHealthCheck(["migrate", "healthcheck"]).Should().BeFalse();
	}

	[Fact]
	public void ResolvePort_SemVariaveis_8080()
		=> SeccoHealthCheckCommand.ResolvePort(null, null).Should().Be(8080);

	[Fact]
	public void ResolvePort_HttpPortsMultiplos_UsaOPrimeiro()
		=> SeccoHealthCheckCommand.ResolvePort(null, "8081;8082").Should().Be(8081);

	[Fact]
	public void ResolvePort_UrlsSobrepoeHttpPorts()
		=> SeccoHealthCheckCommand.ResolvePort("https://+:443;http://+:5000", "8080").Should().Be(5000);

	[Theory]
	[InlineData("abc")]
	[InlineData("0")]
	[InlineData("70000")]
	[InlineData("-1")]
	public void ResolvePort_Invalido_Null(string httpPorts)
		=> SeccoHealthCheckCommand.ResolvePort(null, httpPorts).Should().BeNull();

	[Fact]
	public void ResolvePort_HostCoringaSemPorta_Null()
		=> SeccoHealthCheckCommand.ResolvePort("http://+", null).Should().BeNull();

	[Fact]
	public async Task RunAsync_Status200_Zero()
	{
		var code = await SeccoHealthCheckCommand.RunAsync(8080, Responding(HttpStatusCode.OK), TimeSpan.FromSeconds(5), CancellationToken.None);
		code.Should().Be(0);
	}

	[Fact]
	public async Task RunAsync_Status503_Um()
	{
		var code = await SeccoHealthCheckCommand.RunAsync(8080, Responding(HttpStatusCode.ServiceUnavailable), TimeSpan.FromSeconds(5), CancellationToken.None);
		code.Should().Be(1);
	}

	[Fact]
	public async Task RunAsync_ConexaoRecusada_Um()
	{
		var handler = new FakeHandler((_, _) => throw new HttpRequestException("recusada"));
		var code = await SeccoHealthCheckCommand.RunAsync(8080, handler, TimeSpan.FromSeconds(5), CancellationToken.None);
		code.Should().Be(1);
	}

	[Fact]
	public async Task RunAsync_Timeout_Um()
	{
		var handler = new FakeHandler(async (_, ct) =>
		{
			await Task.Delay(TimeSpan.FromSeconds(30), ct);
			return new HttpResponseMessage(HttpStatusCode.OK);
		});
		var code = await SeccoHealthCheckCommand.RunAsync(8080, handler, TimeSpan.FromMilliseconds(100), CancellationToken.None);
		code.Should().Be(1);
	}
}
