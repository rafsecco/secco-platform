using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Secco.SecureGate.Tests.Integration.Helpers;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Gestão de tenants (Fase 6.3): CRUD protegido pelo scope <c>securegate:admin</c> e
/// write-only para segredos — nenhuma resposta devolve connection strings (ADR-0020).
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class TenantManagementTests(SecureGateApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClientWithScopes(params string[] scopes)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokenFactory.CreateToken(scopes));

        return client;
    }

    private static string UniqueSlug() => $"t-{Guid.NewGuid():N}";

    private static async Task<(Guid Id, string Slug)> CreateTenantAsync(HttpClient admin)
    {
        var slug = UniqueSlug();
        var response = await admin.PostAsJsonAsync("/api/v1/tenants", new { name = "Tenant de teste", slug });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        return (body.GetProperty("id").GetGuid(), slug);
    }

    [Fact]
    public async Task CreateTenant_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/tenants", new { name = "x", slug = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a FallbackPolicy é fail-closed");
    }

    [Fact]
    public async Task CreateTenant_WithoutAdminScope_Returns403()
    {
        // Scope de produto não é scope de gestão (least privilege)
        var response = await CreateClientWithScopes("securegate", "logstream")
            .PostAsJsonAsync("/api/v1/tenants", new { name = "x", slug = UniqueSlug() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateTenant_WithValidPayload_Returns201AndIsRetrievable()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var (id, slug) = await CreateTenantAsync(admin);

        var fetched = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{id}", Json);

        fetched.GetProperty("slug").GetString().Should().Be(slug);
        fetched.GetProperty("isActive").GetBoolean().Should().BeTrue("tenant nasce ativo");
        fetched.GetProperty("products").GetArrayLength().Should().Be(0, "nasce sem bancos cadastrados");
    }

    [Fact]
    public async Task CreateTenant_WithDuplicateSlug_Returns409()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var (_, slug) = await CreateTenantAsync(admin);

        var duplicate = await admin.PostAsJsonAsync("/api/v1/tenants", new { name = "Duplicado", slug });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateTenant_WithInvalidSlug_Returns400()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);

        var response = await admin.PostAsJsonAsync("/api/v1/tenants", new { name = "x", slug = "slug inválido!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpsertDatabase_ThenGetTenant_ListsProductWithoutConnectionString()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var (id, _) = await CreateTenantAsync(admin);
        const string connectionString = "Server=segredo-que-nao-vaza;Database=x;";

        var upsert = await admin.PutAsJsonAsync($"/api/v1/tenants/{id}/databases/logstream",
            new { connectionString });

        upsert.StatusCode.Should().Be(HttpStatusCode.NoContent, "a superfície de gestão é write-only para o segredo");

        var body = await admin.GetStringAsync($"/api/v1/tenants/{id}");
        using var fetched = JsonDocument.Parse(body);

        fetched.RootElement.GetProperty("products").EnumerateArray()
            .Select(p => p.GetString()).Should().Contain("logstream");
        body.Should().NotContain("segredo-que-nao-vaza", "connection strings nunca saem da gestão (ADR-0020)");
    }

    [Fact]
    public async Task UpsertDatabase_ForUnknownTenant_Returns404()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);

        var response = await admin.PutAsJsonAsync($"/api/v1/tenants/{Guid.NewGuid()}/databases/logstream",
            new { connectionString = "Server=x;Database=y;" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpsertDatabase_WithInvalidProduct_Returns400()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var (id, _) = await CreateTenantAsync(admin);

        var response = await admin.PutAsJsonAsync($"/api/v1/tenants/{id}/databases/produto_invalido!",
            new { connectionString = "Server=x;Database=y;" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListTenants_ReturnsCreatedTenants()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var (id, _) = await CreateTenantAsync(admin);

        var tenants = await admin.GetFromJsonAsync<JsonElement>("/api/v1/tenants", Json);

        tenants.EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).Should().Contain(id);
    }
}
