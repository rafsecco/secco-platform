using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Secco.SampleService.Tests.Integration;

public class SampleEndpointsTests(SampleServiceApiFactory factory) : IClassFixture<SampleServiceApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync() => await factory.EnsureTenantDatabasesMigratedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClientForTenant(Guid tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", SampleServiceApiFactory.CreateToken(tenantId));
        return client;
    }

    [Fact]
    public async Task HealthEndpoints_Always_RespondAnonymously()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostSample_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/samples", new { name = "sem token" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a FallbackPolicy protege endpoints sem metadata explícita (ADR-0020)");
    }

    [Fact]
    public async Task PostSample_WithValidToken_Returns201AndRoundTrips()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/samples", new
        {
            name = "primeiro sample",
            description = "criado pelo template",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = created.GetProperty("id").GetGuid();

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/v1/samples/{id}", Json);
        fetched.GetProperty("name").GetString().Should().Be("primeiro sample");
    }

    [Fact]
    public async Task PostSample_WithoutName_Returns400ProblemDetails()
    {
        var client = CreateClientForTenant(factory.TenantAlfa);

        var response = await client.PostAsJsonAsync("/api/v1/samples", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SampleService.Sample.NameRequired");
    }

    [Fact]
    public async Task GetSample_FromAnotherTenant_Returns404BecauseDatabasesAreIsolated()
    {
        var clientAlfa = CreateClientForTenant(factory.TenantAlfa);
        var clientBeta = CreateClientForTenant(factory.TenantBeta);

        var response = await clientAlfa.PostAsJsonAsync("/api/v1/samples", new { name = "segredo do alfa" });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        (await clientBeta.GetAsync($"/api/v1/samples/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "cada tenant possui banco próprio (ADR-0005)");
    }

    [Fact]
    public async Task SearchSamples_ByName_ReturnsPagedMatches()
    {
        var client = CreateClientForTenant(factory.TenantBeta);
        var marker = Guid.NewGuid().ToString("N")[..12];

        await client.PostAsJsonAsync("/api/v1/samples", new { name = $"alvo {marker}" });
        await client.PostAsJsonAsync("/api/v1/samples", new { name = "outro qualquer" });

        var result = await client.GetFromJsonAsync<JsonElement>($"/api/v1/samples?name={marker}", Json);

        result.GetProperty("totalCount").GetInt64().Should().Be(1);
        result.GetProperty("items")[0].GetProperty("name").GetString().Should().Contain(marker);
    }
}
