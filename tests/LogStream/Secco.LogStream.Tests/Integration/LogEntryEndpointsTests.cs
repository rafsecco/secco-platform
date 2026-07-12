using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Secco.LogStream.Tests.Integration.Helpers;

namespace Secco.LogStream.Tests.Integration;

public class LogEntryEndpointsTests(LogStreamApiFactory factory) : IClassFixture<LogStreamApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync() => await factory.EnsureTenantDatabasesMigratedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClientForTenant(Guid tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokenFactory.CreateToken(tenantId));
        return client;
    }

    private static async Task<HttpResponseMessage> WaitUntilFoundAsync(HttpClient client, string url)
    {
        // A persistência é assíncrona (fila + worker): aguarda até o registro aparecer
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            var response = await client.GetAsync(url);

            if (response.StatusCode != HttpStatusCode.NotFound || DateTime.UtcNow > deadline)
            {
                return response;
            }

            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task PostLogEntry_WithValidToken_Returns202AndPersistsAsynchronously()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/log-entries", new
        {
            level = "Error",
            message = "Falha ao processar pagamento",
            stackTrace = "at Pagamentos.Processar()",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = accepted.GetProperty("id").GetGuid();

        var persisted = await WaitUntilFoundAsync(client, $"/api/v1/log-entries/{id}");
        persisted.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await persisted.Content.ReadFromJsonAsync<JsonElement>(Json);
        dto.GetProperty("level").GetString().Should().Be("Error");
        dto.GetProperty("message").GetString().Should().Be("Falha ao processar pagamento");
    }

    [Fact]
    public async Task PostLogEntry_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/log-entries", new
        {
            level = "Information",
            message = "sem token",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostLogEntry_WithEmptyMessage_Returns400ProblemDetails()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/log-entries", new { level = "Error", message = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("LogStream.LogEntry.MessageRequired");
    }

    [Fact]
    public async Task PostLogEntry_WithMessageAboveLimit_Returns400()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/log-entries", new
        {
            level = "Error",
            message = new string('x', 16_385),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MessageTooLong");
    }

    [Fact]
    public async Task PostBatch_WithValidItems_Returns202WithAllIds()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);
        var items = Enumerable.Range(1, 5).Select(i => new { level = "Warning", message = $"batch item {i}" });

        var response = await client.PostAsJsonAsync("/api/v1/log-entries/batch", items);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        accepted.GetProperty("ids").GetArrayLength().Should().Be(5);
    }

    [Fact]
    public async Task PostBatch_AboveLimit_Returns400()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);
        var items = Enumerable.Range(1, 501).Select(i => new { level = "Information", message = "x" });

        var response = await client.PostAsJsonAsync("/api/v1/log-entries/batch", items);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("BatchTooLarge");
    }

    [Fact]
    public async Task GetLogEntry_FromAnotherTenant_Returns404BecauseDatabasesAreIsolated()
    {
        var clientAlfa = CreateClientForTenant(factory.TenantAlfa);
        var clientBeta = CreateClientForTenant(factory.TenantBeta);

        var response = await clientAlfa.PostAsJsonAsync("/api/v1/log-entries", new
        {
            level = "Critical",
            message = "segredo do tenant alfa",
        });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        // Confirma que persistiu para o alfa antes de provar o isolamento
        (await WaitUntilFoundAsync(clientAlfa, $"/api/v1/log-entries/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await clientBeta.GetAsync($"/api/v1/log-entries/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "cada tenant tem banco próprio — o registro do alfa não existe no banco do beta (ADR-0005)");
    }

    [Fact]
    public async Task SearchLogEntries_ByLevelAndMessage_ReturnsOnlyMatches()
    {
        var client = CreateClientForTenant(factory.TenantBeta);
        var marker = Guid.NewGuid().ToString("N");

        await client.PostAsJsonAsync("/api/v1/log-entries", new { level = "Error", message = $"alvo {marker}" });
        await client.PostAsJsonAsync("/api/v1/log-entries", new { level = "Information", message = $"ruido {marker}" });

        // Aguarda os dois registros persistirem
        var deadline = DateTime.UtcNow.AddSeconds(10);
        JsonElement page;

        while (true)
        {
            var all = await client.GetAsync($"/api/v1/log-entries?message={marker}");
            page = await all.Content.ReadFromJsonAsync<JsonElement>(Json);

            if (page.GetProperty("totalCount").GetInt64() >= 2 || DateTime.UtcNow > deadline)
            {
                break;
            }

            await Task.Delay(100);
        }

        var filtered = await client.GetAsync($"/api/v1/log-entries?message={marker}&level=Error");
        var filteredPage = await filtered.Content.ReadFromJsonAsync<JsonElement>(Json);

        filteredPage.GetProperty("totalCount").GetInt64().Should().Be(1);
        filteredPage.GetProperty("items")[0].GetProperty("message").GetString().Should().Contain("alvo");
    }

    [Fact]
    public async Task SearchLogEntries_WithInvertedDateRange_Returns400()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.GetAsync(
            "/api/v1/log-entries?from=2026-07-10T00:00:00Z&to=2026-07-01T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("InvalidDateRange");
    }
}
