using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Secco.AdminPortal.Authentication;
using Secco.AdminPortal.Services;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// O <see cref="SecureGateTenantAdminService"/> anexa o token do operador à chamada do
/// <c>Secco.SecureGate.Client</c> (on-behalf-of, ADR-0023) e projeta o resultado.
/// </summary>
public class SecureGateTenantAdminServiceTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? AuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationHeader = request.Headers.Authorization?.ToString();

            const string json = """
                [{"id":"018f0000-0000-7000-8000-000000000abc","name":"Acme","slug":"acme","isActive":true,"createdAt":"2026-01-02T03:04:05+00:00"}]
                """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (SecureGateTenantAdminService Service, CapturingHandler Handler) BuildService(string? token)
    {
        var handler = new CapturingHandler();

        var services = new ServiceCollection();
        services.AddHttpClient(AdminPortalDefaults.SecureGateHttpClient, client =>
                client.BaseAddress = new Uri("https://securegate.test"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var tokenProvider = Substitute.For<IOperatorTokenProvider>();
        tokenProvider.GetAccessTokenAsync().Returns(token);

        return (new SecureGateTenantAdminService(httpClientFactory, tokenProvider), handler);
    }

    [Fact]
    public async Task ListTenantsAsync_ForwardsOperatorTokenAndProjectsResult()
    {
        var (service, handler) = BuildService("operator-token");

        var tenants = await service.ListTenantsAsync();

        handler.AuthorizationHeader.Should().Be("Bearer operator-token",
            "cada chamada carrega a identidade do operador (ADR-0023)");
        tenants.Should().ContainSingle();
        tenants[0].Name.Should().Be("Acme");
        tenants[0].Slug.Should().Be("acme");
        tenants[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ListTenantsAsync_WithoutToken_DoesNotSendAuthorizationHeader()
    {
        var (service, handler) = BuildService(token: null);

        await service.ListTenantsAsync();

        handler.AuthorizationHeader.Should().BeNull();
    }
}
