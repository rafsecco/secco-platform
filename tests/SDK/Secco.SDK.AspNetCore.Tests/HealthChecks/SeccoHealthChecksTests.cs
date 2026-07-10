using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Secco.SDK.AspNetCore.Extensions;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.HealthChecks;

public class SeccoHealthChecksTests
{
    private static async Task<IHost> StartHostAsync(Action<IHealthChecksBuilder>? configureChecks = null)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    var healthChecksBuilder = services.AddSeccoHealthChecks();
                    configureChecks?.Invoke(healthChecksBuilder);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapSeccoHealthChecks());
                });
            })
            .StartAsync();
    }

    [Fact]
    public async Task Live_WithoutAnyCheckRegistered_ReturnsHealthy()
    {
        using var host = await StartHostAsync();

        var response = await host.GetTestClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Live_EvenWithUnhealthyCheckRegistered_ReturnsHealthy()
    {
        using var host = await StartHostAsync(checks =>
            checks.AddCheck("dependencia", () => HealthCheckResult.Unhealthy()));

        var response = await host.GetTestClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_WithHealthyChecks_ReturnsOkWithChecksInJson()
    {
        using var host = await StartHostAsync(checks =>
            checks.AddCheck("banco-tenant", () => HealthCheckResult.Healthy()));

        var response = await host.GetTestClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Healthy\"");
        body.Should().Contain("\"name\":\"banco-tenant\"");
        body.Should().Contain("durationMs");
    }

    [Fact]
    public async Task Ready_WithUnhealthyCheck_ReturnsServiceUnavailable()
    {
        using var host = await StartHostAsync(checks =>
            checks.AddCheck("dependencia", () => HealthCheckResult.Unhealthy()));

        var response = await host.GetTestClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"status\":\"Unhealthy\"");
    }

    [Fact]
    public async Task Ready_WhenCheckThrows_DoesNotLeakExceptionDetailsInResponse()
    {
        using var host = await StartHostAsync(checks =>
            checks.AddCheck<ThrowingCheck>("explosiva"));

        var response = await host.GetTestClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("SENSITIVE", "mensagens de exceção vazam topologia interna (ADR-0020)");
        body.Should().Contain("\"name\":\"explosiva\"");
    }

    [Fact]
    public async Task Ready_WithoutAnyCheckRegistered_ReturnsHealthy()
    {
        using var host = await StartHostAsync();

        var response = await host.GetTestClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"status\":\"Healthy\"");
    }

    private sealed class ThrowingCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SENSITIVE: Server=sql-prod-03.internal;Password=hunter2;");
    }
}
