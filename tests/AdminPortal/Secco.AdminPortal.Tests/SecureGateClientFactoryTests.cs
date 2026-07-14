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
/// A <see cref="SecureGateClientFactory"/> anexa o access token do operador ao client do
/// SecureGate (on-behalf-of, ADR-0023) — a custódia central do token para toda a gestão.
/// </summary>
public class SecureGateClientFactoryTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? AuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationHeader = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (SecureGateClientFactory Factory, CapturingHandler Handler) Build(string? token)
    {
        var handler = new CapturingHandler();

        var services = new ServiceCollection();
        services.AddHttpClient(AdminPortalDefaults.SecureGateHttpClient, client =>
                client.BaseAddress = new Uri("https://securegate.test"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var tokenProvider = Substitute.For<IOperatorTokenProvider>();
        tokenProvider.GetAccessTokenAsync().Returns(token);

        return (new SecureGateClientFactory(httpClientFactory, tokenProvider), handler);
    }

    [Fact]
    public async Task CreateAsync_WithToken_AttachesBearer()
    {
        var (factory, handler) = Build("operator-token");

        var client = await factory.CreateAsync();
        await client.ListTenantsAsync(CancellationToken.None);

        handler.AuthorizationHeader.Should().Be("Bearer operator-token");
    }

    [Fact]
    public async Task CreateAsync_WithoutToken_DoesNotAttachAuthorization()
    {
        var (factory, handler) = Build(token: null);

        var client = await factory.CreateAsync();
        await client.ListTenantsAsync(CancellationToken.None);

        handler.AuthorizationHeader.Should().BeNull();
    }
}
