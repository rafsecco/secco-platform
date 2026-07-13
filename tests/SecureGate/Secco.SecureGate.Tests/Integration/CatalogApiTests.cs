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
/// Endpoint de catálogo (Fase 6.3): entrega connection strings SOMENTE ao portador do
/// scope <c>catalog:&lt;produto&gt;</c> do produto da rota (least privilege, ADR-0020) e
/// omite tenants desativados — a revogação propaga aos produtos em até um TTL de cache.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class CatalogApiTests(SecureGateApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        await factory.EnsureDatabaseMigratedAsync();
        _admin = CreateClientWithScopes(SecureGateScopes.Admin);
    }

    public Task DisposeAsync()
    {
        _admin?.Dispose();

        return Task.CompletedTask;
    }

    private HttpClient CreateClientWithScopes(params string[] scopes)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokenFactory.CreateToken(scopes));

        return client;
    }

    private async Task<Guid> CreateTenantWithDatabaseAsync(string connectionString)
    {
        var created = await _admin.PostAsJsonAsync("/api/v1/tenants",
            new { name = "Tenant de catálogo", slug = $"t-{Guid.NewGuid():N}" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var upsert = await _admin.PutAsJsonAsync($"/api/v1/tenants/{id}/databases/logstream",
            new { connectionString });
        upsert.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return id;
    }

    [Fact]
    public async Task GetCatalogTenant_WithMatchingScope_ReturnsConnectionString()
    {
        const string connectionString = "Server=catalogo;Database=tenant-a;";
        var tenantId = await CreateTenantWithDatabaseAsync(connectionString);

        var reader = CreateClientWithScopes(SecureGateScopes.CatalogFor("logstream"));
        var entry = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/v1/catalog/logstream/tenants/{tenantId}", Json);

        entry.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        entry.GetProperty("connectionString").GetString().Should().Be(connectionString,
            "o endpoint de catálogo é o ÚNICO que entrega o segredo — mediante o scope certo");
    }

    [Fact]
    public async Task GetCatalogTenant_WithScopeOfAnotherProduct_Returns403()
    {
        var tenantId = await CreateTenantWithDatabaseAsync("Server=x;Database=y;");

        // Scope de catálogo de OUTRO produto não abre este catálogo (least privilege)
        var response = await CreateClientWithScopes(SecureGateScopes.CatalogFor("securegate"))
            .GetAsync($"/api/v1/catalog/logstream/tenants/{tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCatalogTenant_WithProductScopeOnly_Returns403()
    {
        var tenantId = await CreateTenantWithDatabaseAsync("Server=x;Database=y;");

        // O scope de USO do produto (logstream) não é o scope de CATÁLOGO (catalog:logstream)
        var response = await CreateClientWithScopes("logstream")
            .GetAsync($"/api/v1/catalog/logstream/tenants/{tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCatalogTenant_ForUnknownTenant_Returns404()
    {
        var response = await CreateClientWithScopes(SecureGateScopes.CatalogFor("logstream"))
            .GetAsync($"/api/v1/catalog/logstream/tenants/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Catalog_ExcludesDeactivatedTenant()
    {
        var active = await CreateTenantWithDatabaseAsync("Server=ativo;Database=a;");
        var deactivated = await CreateTenantWithDatabaseAsync("Server=desativado;Database=d;");

        (await _admin.PostAsync($"/api/v1/tenants/{deactivated}/deactivate", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reader = CreateClientWithScopes(SecureGateScopes.CatalogFor("logstream"));

        var list = await reader.GetFromJsonAsync<JsonElement>("/api/v1/catalog/logstream/tenants", Json);
        var ids = list.EnumerateArray().Select(e => e.GetProperty("tenantId").GetGuid()).ToList();

        ids.Should().Contain(active);
        ids.Should().NotContain(deactivated, "tenant desativado some do catálogo");

        var single = await reader.GetAsync($"/api/v1/catalog/logstream/tenants/{deactivated}");
        single.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a resposta não distingue desativado de inexistente (ADR-0020)");
    }

    [Fact]
    public async Task GetCatalog_WithInvalidProductInRoute_Returns400()
    {
        // Mesmo com o scope correspondente concedido, o formato do produto é validado
        var response = await CreateClientWithScopes(SecureGateScopes.CatalogFor("produto_invalido"))
            .GetAsync("/api/v1/catalog/produto_invalido/tenants");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
