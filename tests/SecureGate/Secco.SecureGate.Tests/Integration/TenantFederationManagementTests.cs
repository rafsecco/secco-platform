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
/// Gestão da federação de autenticação por tenant (ADR-0026): PUT idempotente gated pelo
/// scope <c>securegate:admin</c>; o directory id NÃO é segredo e aparece na leitura do
/// detalhe (diferente das connection strings, write-only).
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class TenantFederationManagementTests(SecureGateApiFactory factory) : IAsyncLifetime
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

    private async Task<Guid> CreateTenantAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/tenants",
            new { name = "Tenant federado", slug = $"t-{Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task UpsertFederation_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient()
            .PutAsJsonAsync($"/api/v1/tenants/{Guid.NewGuid()}/federation",
                new { directoryId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpsertFederation_WithoutAdminScope_Returns403()
    {
        var response = await CreateClientWithScopes("logstream")
            .PutAsJsonAsync($"/api/v1/tenants/{Guid.NewGuid()}/federation",
                new { directoryId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpsertFederation_ForUnknownTenant_Returns404()
    {
        var response = await CreateClientWithScopes(SecureGateScopes.Admin)
            .PutAsJsonAsync($"/api/v1/tenants/{Guid.NewGuid()}/federation",
                new { directoryId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpsertFederation_WithoutDirectoryId_Returns400()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var tenantId = await CreateTenantAsync(admin);

        var response = await admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/federation",
            new { enabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTenant_WithoutFederation_ReturnsNullFederation()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var tenantId = await CreateTenantAsync(admin);

        var fetched = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}", Json);

        fetched.GetProperty("federation").ValueKind.Should().Be(JsonValueKind.Null,
            "a federação é opt-in — tenant nasce sem ela");
    }

    [Fact]
    public async Task UpsertFederation_ThenGetTenant_ReturnsDirectoryIdInDetail()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var tenantId = await CreateTenantAsync(admin);
        var directoryId = Guid.NewGuid();

        var upsert = await admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/federation",
            new { directoryId });
        upsert.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var fetched = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}", Json);
        var federation = fetched.GetProperty("federation");

        federation.GetProperty("provider").GetString().Should().Be("entra-id");
        federation.GetProperty("directoryId").GetGuid().Should().Be(directoryId,
            "o directory id não é segredo — é o tid esperado do token (ADR-0026)");
        federation.GetProperty("isEnabled").GetBoolean().Should().BeTrue("enabled ausente = habilitada");
    }

    [Fact]
    public async Task UpsertFederation_Twice_UpdatesDirectoryAndEnablement()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var tenantId = await CreateTenantAsync(admin);
        var newDirectoryId = Guid.NewGuid();

        (await admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/federation",
            new { directoryId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/federation",
            new { directoryId = newDirectoryId, enabled = false });
        second.StatusCode.Should().Be(HttpStatusCode.NoContent, "o PUT é idempotente (ADR-0026)");

        var fetched = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}", Json);
        var federation = fetched.GetProperty("federation");

        federation.GetProperty("directoryId").GetGuid().Should().Be(newDirectoryId);
        federation.GetProperty("isEnabled").GetBoolean().Should().BeFalse();
    }
}
