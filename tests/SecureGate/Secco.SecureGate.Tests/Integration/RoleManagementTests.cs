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
/// Gestão de roles e permissões por tenant (Fase 6.4, ADR-0021): CRUD protegido pelo
/// scope <c>securegate:admin</c>, permissões validadas no formato canônico e substituição
/// idempotente por PUT.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class RoleManagementTests(SecureGateApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient _admin = null!;
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        await factory.EnsureDatabaseMigratedAsync();

        _admin = CreateClientWithScopes(SecureGateScopes.Admin);

        var created = await _admin.PostAsJsonAsync("/api/v1/tenants",
            new { name = "Tenant de roles", slug = $"t-{Guid.NewGuid():N}" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        _tenantId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
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
    public async Task CreateRole_WithoutAdminScope_Returns403()
    {
        var response = await CreateClientWithScopes(SecureGateScopes.AuthorizationRead)
            .PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles", new { name = "intruso" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "o scope de leitura de permissões não concede gestão");
    }

    [Fact]
    public async Task CreateRole_WithValidName_Returns201AndAppearsInList()
    {
        var create = await _admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles", new { name = "auditor" });

        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var roles = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/roles", Json);

        roles.EnumerateArray().Select(r => r.GetProperty("name").GetString())
            .Should().Contain("auditor");
    }

    [Fact]
    public async Task CreateRole_Duplicated_Returns409()
    {
        (await _admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles", new { name = "repetido" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Duplicidade é por nome NORMALIZADO — variação de caixa não cria outro role
        (await _admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles", new { name = "REPETIDO" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateRole_WithSpaceInName_Returns400()
    {
        // Espaço é proibido por construção: o nome viaja na lista space-separated dos clients
        var response = await _admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles",
            new { name = "nome com espaço" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateRole_ForUnknownTenant_Returns404()
    {
        var response = await _admin.PostAsJsonAsync($"/api/v1/tenants/{Guid.NewGuid()}/roles",
            new { name = "orfao" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetPermissions_ReplacesSetIdempotently()
    {
        (await _admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles", new { name = "operador" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await _admin.PutAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles/operador/permissions",
            new { permissions = new[] { "log-entries:read", "log-entries:write" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // PUT com o conjunto reduzido REVOGA o que ficou de fora
        (await _admin.PutAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles/operador/permissions",
            new { permissions = new[] { "log-entries:read" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var roles = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/roles", Json);
        var operador = roles.EnumerateArray().Single(r => r.GetProperty("name").GetString() == "operador");

        operador.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().BeEquivalentTo(["log-entries:read"]);
    }

    [Fact]
    public async Task SetPermissions_WithMalformedPermission_Returns400()
    {
        (await _admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles", new { name = "estrito" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _admin.PutAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles/estrito/permissions",
            new { permissions = new[] { "SemFormato" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "permissões fora do formato canônico recurso:acao são rejeitadas (ADR-0020)");
    }

    [Fact]
    public async Task SetPermissions_ForUnknownRole_Returns404()
    {
        var response = await _admin.PutAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles/fantasma/permissions",
            new { permissions = new[] { "log-entries:read" } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
