using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// Smoke test: o AdminPortal compõe (grafo de DI válido, OIDC + Blazor Server) e expõe o
/// liveness anônimo. Prova que o produto sobe sem depender do SecureGate no ar.
/// </summary>
public class HealthCheckTests : IClassFixture<HealthCheckTests.AdminPortalFactory>
{
    private readonly AdminPortalFactory _factory;

    public HealthCheckTests(AdminPortalFactory factory) => _factory = factory;

    public sealed class AdminPortalFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Config OIDC de teste (o discovery só é buscado num challenge, não no startup)
                    ["Secco:SecureGate:Authority"] = "https://localhost/securegate",
                    ["Secco:SecureGate:ApiBaseUrl"] = "https://localhost/securegate",
                    ["Secco:SecureGate:ClientId"] = "secco-adminportal",
                    ["Secco:SecureGate:ClientSecret"] = "test-secret-with-32-characters!!!",
                }));
        }
    }

    [Fact]
    public async Task Liveness_IsAnonymousAndHealthy()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
