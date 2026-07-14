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
/// O <see cref="LogStreamQueryService"/> lê logs on-behalf-of o operador (ADR-0024): anexa o
/// token do operador E o header <c>X-Tenant-Id</c> do tenant alvo (o operador é tenant-less
/// no token — escolhe o tenant por requisição), e projeta o resultado paginado do LogStream.
/// </summary>
public class LogQueryServiceTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? AuthorizationHeader { get; private set; }
        public string? TenantHeader { get; private set; }
        public string? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            TenantHeader = request.Headers.TryGetValues("X-Tenant-Id", out var values) ? string.Join(",", values) : null;
            RequestUri = request.RequestUri?.ToString();

            const string json = """
                {"items":[{"id":"018f0000-0000-7000-8000-0000000000aa","level":"Warning","message":"boom","stackTrace":null,"correlationId":null,"createdAt":"2026-01-02T03:04:05+00:00"}],"page":1,"size":25,"totalCount":1,"totalPages":1}
                """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (LogStreamQueryService Service, CapturingHandler Handler) Build(string? token)
    {
        var handler = new CapturingHandler();

        var services = new ServiceCollection();
        services.AddHttpClient(AdminPortalDefaults.LogStreamHttpClient, client =>
                client.BaseAddress = new Uri("https://logstream.test"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var tokenProvider = Substitute.For<IOperatorTokenProvider>();
        tokenProvider.GetAccessTokenAsync().Returns(token);

        return (new LogStreamQueryService(httpClientFactory, tokenProvider), handler);
    }

    [Fact]
    public async Task SearchLogEntries_ForwardsTokenAndTenantHeaderAndProjects()
    {
        var (service, handler) = Build("operator-token");
        var tenantId = Guid.NewGuid();

        var page = await service.SearchLogEntriesAsync(tenantId, new LogEntryFilter(Message: "boom"));

        handler.AuthorizationHeader.Should().Be("Bearer operator-token", "cada leitura carrega a identidade do operador");
        handler.TenantHeader.Should().Be(tenantId.ToString(), "o tenant alvo viaja no X-Tenant-Id (ADR-0024)");

        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle();
        page.Items[0].Message.Should().Be("boom");
        page.Items[0].Level.Should().Be("Warning");
    }

    [Fact]
    public async Task SearchLogEntries_PassesFiltersAndPagingOnTheQuery()
    {
        var (service, handler) = Build("operator-token");

        await service.SearchLogEntriesAsync(Guid.NewGuid(),
            new LogEntryFilter(Level: "Warning", Message: "disk", Page: 2, Size: 25));

        handler.RequestUri.Should().Contain("level=Warning").And.Contain("message=disk").And.Contain("page=2");
    }

    [Fact]
    public async Task SearchLogEntries_WithoutToken_DoesNotSendAuthorization()
    {
        var (service, handler) = Build(token: null);

        await service.SearchLogEntriesAsync(Guid.NewGuid(), new LogEntryFilter());

        handler.AuthorizationHeader.Should().BeNull();
        handler.TenantHeader.Should().NotBeNull("o tenant alvo é sempre enviado, mesmo sem token");
    }
}
