using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Secco.SDK.AspNetCore.Correlation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Correlation;

public class SeccoCorrelationMiddlewareTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services => services.AddSeccoCorrelation());
                webBuilder.Configure(app =>
                {
                    app.UseSeccoCorrelation();
                    app.Run(async context =>
                    {
                        var correlationContext = context.RequestServices.GetRequiredService<ICorrelationContext>();
                        await context.Response.WriteAsync(correlationContext.CorrelationId);
                    });
                });
            })
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Request_WithoutHeader_GeneratesGuidVersion7()
    {
        var response = await _client.GetAsync("/");

        var headerValue = response.Headers.GetValues(SeccoHeaders.CorrelationId).Single();
        Guid.Parse(headerValue).Version.Should().Be(7);
    }

    [Fact]
    public async Task Request_WithoutHeader_ExposesSameIdThroughCorrelationContext()
    {
        var response = await _client.GetAsync("/");

        var headerValue = response.Headers.GetValues(SeccoHeaders.CorrelationId).Single();
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Be(headerValue);
    }

    [Fact]
    public async Task Request_WithValidGuidHeader_ReusesSameIdInResponse()
    {
        var incoming = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(SeccoHeaders.CorrelationId, incoming.ToString());

        var response = await _client.SendAsync(request);

        var headerValue = response.Headers.GetValues(SeccoHeaders.CorrelationId).Single();
        headerValue.Should().Be(incoming.ToString());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Request_WithInvalidHeader_GeneratesNewIdInsteadOfReusingIt(string invalidValue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(SeccoHeaders.CorrelationId, invalidValue);

        var response = await _client.SendAsync(request);

        var headerValue = response.Headers.GetValues(SeccoHeaders.CorrelationId).Single();
        headerValue.Should().NotBe(invalidValue);
        Guid.TryParse(headerValue, out _).Should().BeTrue();
    }
}
