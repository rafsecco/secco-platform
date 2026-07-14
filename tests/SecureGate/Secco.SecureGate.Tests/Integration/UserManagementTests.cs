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
/// Provisionamento de usuários (Fase 6.5): criação por administrador (scope
/// <c>securegate:admin</c>), sem auto-registro; senhas nunca voltam nas respostas (ADR-0020).
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class UserManagementTests(SecureGateApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ValidPassword = "Str0ng@Pass!";

    public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClientWithScopes(params string[] scopes)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokenFactory.CreateToken(scopes));

        return client;
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@secco.test";

    private async Task<Guid> CreateTenantAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/tenants",
            new { name = "Tenant de usuários", slug = $"t-{Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task CreateUser_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync($"/api/v1/tenants/{Guid.NewGuid()}/users",
                new { email = UniqueEmail(), password = ValidPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateUser_WithoutAdminScope_Returns403()
    {
        var response = await CreateClientWithScopes("logstream")
            .PostAsJsonAsync($"/api/v1/tenants/{Guid.NewGuid()}/users",
                new { email = UniqueEmail(), password = ValidPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateUser_WithValidPayload_Returns201AndListsWithoutSecret()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var tenantId = await CreateTenantAsync(admin);
        var email = UniqueEmail();

        var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
            new { email, password = ValidPassword });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.Content.ReadAsStringAsync();
        body.Should().NotContain(ValidPassword, "a senha nunca volta na resposta (ADR-0020)");

        var users = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/users", Json);
        users.EnumerateArray().Select(u => u.GetProperty("email").GetString()).Should().Contain(email);
    }

    [Fact]
    public async Task CreateUser_WithRole_AssignsIt()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var tenantId = await CreateTenantAsync(admin);

        (await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/roles", new { name = "operador" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var email = UniqueEmail();
        var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
            new { email, password = ValidPassword, roles = new[] { "operador" } });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        dto.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("operador");
    }

    [Fact]
    public async Task CreateUser_WithUnknownRole_Returns400()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var tenantId = await CreateTenantAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
            new { email = UniqueEmail(), password = ValidPassword, roles = new[] { "inexistente" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateUser_WithDuplicateEmail_Returns409()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var tenantId = await CreateTenantAsync(admin);
        var email = UniqueEmail();

        (await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users", new { email, password = ValidPassword }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
            new { email, password = ValidPassword });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict, "e-mail/username é único global (ADR-0022)");
    }

    [Fact]
    public async Task CreateUser_WithWeakPassword_Returns400()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var tenantId = await CreateTenantAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
            new { email = UniqueEmail(), password = "weak" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateUser_ForUnknownTenant_Returns404()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);

        var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{Guid.NewGuid()}/users",
            new { email = UniqueEmail(), password = ValidPassword });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateUser_WithInvalidEmail_Returns400()
    {
        var admin = CreateClientWithScopes(SecureGateScopes.Admin);
        var tenantId = await CreateTenantAsync(admin);

        var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
            new { email = "não-é-email", password = ValidPassword });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
