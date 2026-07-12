using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.LogStream.Tests.Integration.Helpers;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

public class ApiCallLogEndpointsTests(LogStreamApiFactory factory) : IClassFixture<LogStreamApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync() => await factory.EnsureTenantDatabasesMigratedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokenFactory.CreateToken(factory.TenantAlfa));
        return client;
    }

    private static async Task<JsonElement> WaitUntilPersistedAsync(HttpClient client, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            var response = await client.GetAsync($"/api/v1/api-call-logs/{id}");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"ApiCallLog {id} não persistiu a tempo.");
            }

            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task PostApiCallLog_WithSensitiveHeader_PersistsRedacted()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/api-call-logs", new
        {
            url = "https://pagamentos.exemplo.com/v1/charges",
            httpMethod = "POST",
            isSuccess = false,
            requestHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer token-super-secreto",
                ["Content-Type"] = "application/json",
            },
            responseStatusCode = 429,
            durationMs = 350,
            errorMessage = "rate limited",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var dto = await WaitUntilPersistedAsync(client, id);

        dto.GetProperty("httpMethod").GetString().Should().Be("POST");
        dto.GetProperty("responseStatusCode").GetInt32().Should().Be(429);

        var headers = dto.GetProperty("requestHeaders").GetString()!;
        headers.Should().NotContain("token-super-secreto", "o segredo jamais chega ao banco (ADR-0020)");
        headers.Should().Contain("[REDACTED]");
        headers.Should().Contain("application/json");
    }

    [Fact]
    public async Task PostApiCallLog_WithMalformedUrl_Returns400()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/api-call-logs", new
        {
            url = "nao-e-url",
            httpMethod = "GET",
            isSuccess = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("UrlMalformed");
    }

    [Fact]
    public async Task SearchApiCallLogs_ByIsSuccessFalse_ReturnsOnlyFailures()
    {
        var client = CreateClient();
        var marker = Guid.NewGuid().ToString("N")[..12];

        var ok = await client.PostAsJsonAsync("/api/v1/api-call-logs", new
        {
            url = $"https://api.exemplo.com/{marker}/ok",
            httpMethod = "GET",
            isSuccess = true,
        });
        var fail = await client.PostAsJsonAsync("/api/v1/api-call-logs", new
        {
            url = $"https://api.exemplo.com/{marker}/fail",
            httpMethod = "GET",
            isSuccess = false,
            errorMessage = "timeout",
        });

        var okId = (await ok.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        var failId = (await fail.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        await WaitUntilPersistedAsync(client, okId);
        await WaitUntilPersistedAsync(client, failId);

        var failures = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/api-call-logs?url={marker}&isSuccess=false", Json);

        failures.GetProperty("totalCount").GetInt64().Should().Be(1);
        failures.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(failId);
    }

    [Fact]
    public async Task GetApiCallLog_Unknown_Returns404()
    {
        var response = await CreateClient().GetAsync($"/api/v1/api-call-logs/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
