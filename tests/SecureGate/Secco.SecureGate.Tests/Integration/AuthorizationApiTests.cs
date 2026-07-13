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
/// Endpoint de resolução <c>(tenant, role) → permissões</c> (Fase 6.4, ADR-0021):
/// protegido pelo scope único <c>authorization:read</c>; role desconhecido responde
/// lista vazia — sem revelar o modelo de roles do tenant (ADR-0020).
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class AuthorizationApiTests(SecureGateApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient _admin = null!;
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        await factory.EnsureDatabaseMigratedAsync();

        _admin = CreateClientWithScopes(SecureGateScopes.Admin);

        var created = await _admin.PostAsJsonAsync("/api/v1/tenants",
            new { name = "Tenant de resolução", slug = $"t-{Guid.NewGuid():N}" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        _tenantId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        (await _admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles", new { name = "leitor" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await _admin.PutAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles/leitor/permissions",
            new { permissions = new[] { "log-entries:read" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
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

    [Fact]
    public async Task GetPermissions_WithAuthorizationReadScope_ReturnsRolePermissions()
    {
        var reader = CreateClientWithScopes(SecureGateScopes.AuthorizationRead);

        var permissions = await reader.GetFromJsonAsync<string[]>(
            $"/api/v1/authorization/tenants/{_tenantId}/roles/leitor/permissions", Json);

        permissions.Should().BeEquivalentTo(["log-entries:read"]);
    }

    [Fact]
    public async Task GetPermissions_WithoutAuthorizationReadScope_Returns403()
    {
        // Nem o scope de produto nem o de catálogo abrem a resolução
        var response = await CreateClientWithScopes("logstream", SecureGateScopes.CatalogFor("logstream"))
            .GetAsync($"/api/v1/authorization/tenants/{_tenantId}/roles/leitor/permissions");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPermissions_ForUnknownRole_ReturnsEmptyList()
    {
        var reader = CreateClientWithScopes(SecureGateScopes.AuthorizationRead);

        var permissions = await reader.GetFromJsonAsync<string[]>(
            $"/api/v1/authorization/tenants/{_tenantId}/roles/inexistente/permissions", Json);

        permissions.Should().BeEmpty(
            "role desconhecido e role sem permissões são equivalentes — e a resposta não revela o modelo (ADR-0020)");
    }

    [Fact]
    public async Task GetPermissions_RoleNameMatchingIsCaseInsensitive()
    {
        var reader = CreateClientWithScopes(SecureGateScopes.AuthorizationRead);

        var permissions = await reader.GetFromJsonAsync<string[]>(
            $"/api/v1/authorization/tenants/{_tenantId}/roles/LEITOR/permissions", Json);

        permissions.Should().BeEquivalentTo(["log-entries:read"],
            "a busca usa o nome normalizado do Identity");
    }

    [Fact]
    public async Task GetPermissions_WithMalformedRole_Returns400()
    {
        var reader = CreateClientWithScopes(SecureGateScopes.AuthorizationRead);

        var response = await reader.GetAsync(
            $"/api/v1/authorization/tenants/{_tenantId}/roles/{Uri.EscapeDataString("role inválido!")}/permissions");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPermissions_IsScopedToTheTenant()
    {
        // O MESMO nome de role em outro tenant não vaza permissões (ADR-0005/0021)
        var otherTenant = await _admin.PostAsJsonAsync("/api/v1/tenants",
            new { name = "Outro tenant", slug = $"t-{Guid.NewGuid():N}" });
        var otherTenantId = (await otherTenant.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetGuid();

        var reader = CreateClientWithScopes(SecureGateScopes.AuthorizationRead);

        var permissions = await reader.GetFromJsonAsync<string[]>(
            $"/api/v1/authorization/tenants/{otherTenantId}/roles/leitor/permissions", Json);

        permissions.Should().BeEmpty("o role 'leitor' só existe no primeiro tenant");
    }
}
