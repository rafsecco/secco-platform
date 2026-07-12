using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.LogStream.Tests.Integration.Helpers;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

public class LogProcessEndpointsTests(LogStreamApiFactory factory) : IClassFixture<LogStreamApiFactory>, IAsyncLifetime
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

    private static async Task<JsonElement> WaitForProcessAsync(
        HttpClient client, Guid id, Func<JsonElement, bool>? until = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (true)
        {
            var response = await client.GetAsync($"/api/v1/log-processes/{id}");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

                if (until is null || until(dto) || DateTime.UtcNow > deadline)
                {
                    return dto;
                }
            }
            else if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Processo {id} não persistiu a tempo.");
            }

            await Task.Delay(100);
        }
    }

    private async Task<Guid> CreateProcessAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/log-processes", new { name });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task PostLogProcess_WithValidName_Returns202AndPersistsWithSuccessStatus()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/log-processes", new
        {
            name = "ImportacaoPedidos",
            externalReference = "job-2026-4411",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var dto = await WaitForProcessAsync(client, id);
        dto.GetProperty("name").GetString().Should().Be("ImportacaoPedidos");
        dto.GetProperty("externalReference").GetString().Should().Be("job-2026-4411");
        dto.GetProperty("status").GetString().Should().Be("Success");
        dto.GetProperty("detailCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task PostLogProcess_WithoutName_Returns400()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/log-processes", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("NameRequired");
    }

    [Fact]
    public async Task PostDetails_WithWarningAndError_AggregatesStatusToError()
    {
        var client = CreateClient();
        var id = await CreateProcessAsync(client, "FechamentoDiario");

        // O 202 do pai já habilita details — a fila FIFO garante a ordem de persistência
        (await client.PostAsJsonAsync($"/api/v1/log-processes/{id}/details", new
        {
            level = "Warning",
            message = "estoque baixo",
        })).StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await client.PostAsJsonAsync($"/api/v1/log-processes/{id}/details", new
        {
            level = "Error",
            message = "falha ao emitir nota",
            stackTrace = "at Notas.Emitir()",
        })).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var dto = await WaitForProcessAsync(client, id, d => d.GetProperty("detailCount").GetInt32() == 2);

        dto.GetProperty("detailCount").GetInt32().Should().Be(2);
        dto.GetProperty("status").GetString().Should().Be("Error", "o status agregado é o pior nível entre os details");
    }

    [Fact]
    public async Task PostDetailsBatch_Returns202AndPersistsAll()
    {
        var client = CreateClient();
        var id = await CreateProcessAsync(client, "SincronizacaoCatalogo");

        var response = await client.PostAsJsonAsync($"/api/v1/log-processes/{id}/details/batch",
            Enumerable.Range(1, 4).Select(i => new { level = "Information", message = $"passo {i}" }));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("ids").GetArrayLength().Should().Be(4);

        await WaitForProcessAsync(client, id, d => d.GetProperty("detailCount").GetInt32() == 4);

        var details = await client.GetFromJsonAsync<JsonElement>($"/api/v1/log-processes/{id}/details?size=10", Json);
        details.GetProperty("totalCount").GetInt64().Should().Be(4);
    }

    [Fact]
    public async Task SearchLogProcesses_FilteredByStatus_ReturnsOnlyMatching()
    {
        var client = CreateClient();
        var marker = Guid.NewGuid().ToString("N")[..12];

        var healthyId = await CreateProcessAsync(client, $"proc-ok-{marker}");
        var failingId = await CreateProcessAsync(client, $"proc-err-{marker}");

        await client.PostAsJsonAsync($"/api/v1/log-processes/{failingId}/details", new
        {
            level = "Critical",
            message = "explodiu",
        });

        await WaitForProcessAsync(client, healthyId);
        await WaitForProcessAsync(client, failingId, d => d.GetProperty("detailCount").GetInt32() == 1);

        var critical = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/log-processes?name={marker}&status=Critical", Json);
        critical.GetProperty("totalCount").GetInt64().Should().Be(1);
        critical.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(failingId);

        var success = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/log-processes?name={marker}&status=Success", Json);
        success.GetProperty("totalCount").GetInt64().Should().Be(1);
        success.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(healthyId);
    }

    [Fact]
    public async Task GetDetails_OfUnknownProcess_Returns404()
    {
        var client = CreateClient();

        var response = await client.GetAsync($"/api/v1/log-processes/{Guid.NewGuid()}/details");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostDetail_ForUnknownProcess_IsAcceptedFireAndForget()
    {
        var client = CreateClient();

        // Ingestão é fire-and-forget: o 202 aceita; a falha de FK acontece no worker e é logada
        var response = await client.PostAsJsonAsync($"/api/v1/log-processes/{Guid.NewGuid()}/details", new
        {
            level = "Information",
            message = "orfão",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
