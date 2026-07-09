using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Tenancy;

public class SeccoTenancyMiddlewareTests
{
    private static async Task<IHost> StartHostAsync(string? claimValue = null)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services => services.AddSeccoTenancy());
                webBuilder.Configure(app =>
                {
                    // Simula o middleware de autenticação populando a claim tenant_id
                    app.Use(async (context, nextMiddleware) =>
                    {
                        if (claimValue is not null)
                        {
                            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(SeccoClaims.TenantId, claimValue)], authenticationType: "test"));
                        }

                        await nextMiddleware();
                    });

                    app.UseSeccoTenancy();
                    app.Run(async context =>
                    {
                        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
                        await context.Response.WriteAsync(
                            tenantContext.IsResolved ? tenantContext.TenantId.ToString()! : "unresolved");
                    });
                });
            })
            .StartAsync();
    }

    [Fact]
    public async Task Request_WithoutClaimOrHeader_PassesThroughUnresolved()
    {
        using var host = await StartHostAsync();

        var response = await host.GetTestClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("unresolved");
    }

    [Fact]
    public async Task Request_WithValidClaim_ResolvesTenantFromClaim()
    {
        var tenantId = Guid.NewGuid();
        using var host = await StartHostAsync(claimValue: tenantId.ToString());

        var response = await host.GetTestClient().GetAsync("/");

        (await response.Content.ReadAsStringAsync()).Should().Be(tenantId.ToString());
    }

    [Fact]
    public async Task Request_WithValidHeaderAndNoClaim_ResolvesTenantFromHeader()
    {
        var tenantId = Guid.NewGuid();
        using var host = await StartHostAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(SeccoHeaders.TenantId, tenantId.ToString());

        var response = await host.GetTestClient().SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be(tenantId.ToString());
    }

    [Fact]
    public async Task Request_WithClaimAndMatchingHeader_ResolvesWithoutConflict()
    {
        var tenantId = Guid.NewGuid();
        using var host = await StartHostAsync(claimValue: tenantId.ToString());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(SeccoHeaders.TenantId, tenantId.ToString());

        var response = await host.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(tenantId.ToString());
    }

    [Fact]
    public async Task Request_WithClaimAndDivergentHeader_ReturnsBadRequestProblemDetails()
    {
        using var host = await StartHostAsync(claimValue: Guid.NewGuid().ToString());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(SeccoHeaders.TenantId, Guid.NewGuid().ToString());

        var response = await host.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Request_WithInvalidHeaderAndNoClaim_PassesThroughUnresolved()
    {
        using var host = await StartHostAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(SeccoHeaders.TenantId, "not-a-guid");

        var response = await host.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("unresolved");
    }
}
