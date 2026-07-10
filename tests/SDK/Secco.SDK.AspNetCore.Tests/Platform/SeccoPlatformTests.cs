using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Secco.SDK.AspNetCore.Correlation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Platform;

public class SeccoPlatformTests
{
    private static async Task<IHost> StartHostAsync()
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSeccoPlatform();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseSeccoPlatform();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapSeccoPlatform();
                        endpoints.MapGet("/contextos", (ICorrelationContext correlation, ITenantContext tenant) =>
                            $"{correlation.CorrelationId}|{(tenant.IsResolved ? tenant.TenantId.ToString() : "sem-tenant")}");
                    });
                });
            })
            .StartAsync();
    }

    [Fact]
    public async Task Pipeline_Always_PopulatesCorrelationAndEchoesHeader()
    {
        using var host = await StartHostAsync();

        var response = await host.GetTestClient().GetAsync("/contextos");

        var headerValue = response.Headers.GetValues(SeccoHeaders.CorrelationId).Single();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().StartWith(headerValue);
    }

    [Fact]
    public async Task Pipeline_WithTenantHeader_ResolvesTenantAfterCorrelation()
    {
        var tenantId = Guid.NewGuid();
        using var host = await StartHostAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/contextos");
        request.Headers.Add(SeccoHeaders.TenantId, tenantId.ToString());

        var response = await host.GetTestClient().SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().EndWith($"|{tenantId}");
    }

    [Fact]
    public async Task MapSeccoPlatform_Always_ExposesHealthEndpoints()
    {
        using var host = await StartHostAsync();
        var client = host.GetTestClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void AddSeccoPlatform_CalledTwice_RegistersEverythingOnlyOnce()
    {
        var services = new ServiceCollection();

        services.AddSeccoPlatform();
        services.AddSeccoPlatform();

        services.Count(d => d.ServiceType == typeof(SeccoPlatformServiceCollectionExtensions.SeccoPlatformMarker))
            .Should().Be(1);
        services.Count(d => d.ServiceType == typeof(SeccoResilienceServiceCollectionExtensions.SeccoResilienceMarker))
            .Should().Be(1, "dois pipelines de resiliência empilhados multiplicariam os retries");
    }

    [Fact]
    public void AddSeccoResilience_CustomizedBeforeAddSeccoPlatform_IsNotDuplicated()
    {
        var services = new ServiceCollection();

        services.AddSeccoResilience(options => options.Retry.MaxRetryAttempts = 5);
        services.AddSeccoPlatform();

        services.Count(d => d.ServiceType == typeof(SeccoResilienceServiceCollectionExtensions.SeccoResilienceMarker))
            .Should().Be(1);
    }
}
