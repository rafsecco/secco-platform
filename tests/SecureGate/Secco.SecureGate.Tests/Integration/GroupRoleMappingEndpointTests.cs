using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// CRUD do mapeamento grupo→perfil (issue #28, ADR-0036). Sem reconciliação automática nesta
/// entrega — o mapeamento só existe, ainda não sincroniza sozinho.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class GroupRoleMappingEndpointTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient AdminClient()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.Admin));

		return client;
	}

	private async Task<Guid> TenantAsync(HttpClient admin)
	{
		var response = await admin.PostAsJsonAsync("/api/v1/tenants",
			new { name = "Tenant de mapeamento", slug = $"t-{Guid.NewGuid():N}" });
		response.StatusCode.Should().Be(HttpStatusCode.Created);

		return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
	}

	private async Task EnableFederationAsync(HttpClient admin, Guid tenantId) =>
		(await admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/federation",
			new { directoryId = Guid.NewGuid(), enabled = true }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

	private static async Task CreateRoleAsync(HttpClient admin, Guid tenantId, string name) =>
		(await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/roles", new { name }))
			.StatusCode.Should().Be(HttpStatusCode.Created);

	[Fact]
	public async Task CreateMapping_SemFederacao_Responde409()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		await CreateRoleAsync(admin, tenantId, "financeiro-leitor");

		var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/entra/group-role-mappings",
			new { entraGroupId = Guid.NewGuid(), entraGroupDisplayName = "Financeiro", roleName = "financeiro-leitor" });

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task CreateMapping_ComTudoValido_Responde201EAparecendoNaListagem()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		await EnableFederationAsync(admin, tenantId);
		await CreateRoleAsync(admin, tenantId, "financeiro-leitor");
		var groupId = Guid.NewGuid();

		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/entra/group-role-mappings",
			new { entraGroupId = groupId, entraGroupDisplayName = "Financeiro", roleName = "financeiro-leitor" });

		created.StatusCode.Should().Be(HttpStatusCode.Created);
		var body = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
		body.GetProperty("entraGroupId").GetGuid().Should().Be(groupId);
		body.GetProperty("roleName").GetString().Should().Be("financeiro-leitor");

		var list = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/entra/group-role-mappings", Json);
		list.EnumerateArray().Should().ContainSingle(m => m.GetProperty("entraGroupId").GetGuid() == groupId);
	}

	[Fact]
	public async Task CreateMapping_GrupoJaMapeado_Responde409()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		await EnableFederationAsync(admin, tenantId);
		await CreateRoleAsync(admin, tenantId, "financeiro-leitor");
		await CreateRoleAsync(admin, tenantId, "financeiro-admin");
		var groupId = Guid.NewGuid();

		(await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/entra/group-role-mappings",
			new { entraGroupId = groupId, entraGroupDisplayName = "Financeiro", roleName = "financeiro-leitor" }))
			.StatusCode.Should().Be(HttpStatusCode.Created);

		var second = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/entra/group-role-mappings",
			new { entraGroupId = groupId, entraGroupDisplayName = "Financeiro (outro nome)", roleName = "financeiro-admin" });

		second.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task CreateMapping_PerfilReservado_Responde400()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		await EnableFederationAsync(admin, tenantId);

		var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/entra/group-role-mappings",
			new { entraGroupId = Guid.NewGuid(), entraGroupDisplayName = "Financeiro", roleName = "installation-log-reader" });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task DeleteMapping_Existente_Responde204ESomeDaListagem()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		await EnableFederationAsync(admin, tenantId);
		await CreateRoleAsync(admin, tenantId, "financeiro-leitor");

		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/entra/group-role-mappings",
			new { entraGroupId = Guid.NewGuid(), entraGroupDisplayName = "Financeiro", roleName = "financeiro-leitor" });
		var mappingId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

		(await admin.DeleteAsync($"/api/v1/tenants/{tenantId}/entra/group-role-mappings/{mappingId}"))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var list = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/entra/group-role-mappings", Json);
		list.EnumerateArray().Should().BeEmpty();
	}

	[Fact]
	public async Task DeleteMapping_Inexistente_Responde404()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);

		var response = await admin.DeleteAsync($"/api/v1/tenants/{tenantId}/entra/group-role-mappings/{Guid.NewGuid()}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task DeleteMapping_DeOutroTenant_Responde404ESobrevive()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		var outroTenantId = await TenantAsync(admin);
		await EnableFederationAsync(admin, tenantId);
		await CreateRoleAsync(admin, tenantId, "financeiro-leitor");

		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/entra/group-role-mappings",
			new { entraGroupId = Guid.NewGuid(), entraGroupDisplayName = "Financeiro", roleName = "financeiro-leitor" });
		var mappingId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

		var response = await admin.DeleteAsync($"/api/v1/tenants/{outroTenantId}/entra/group-role-mappings/{mappingId}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
		var list = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/entra/group-role-mappings", Json);
		list.EnumerateArray().Should().ContainSingle();
	}

	[Fact]
	public async Task ListMappings_SemToken_Responde401()
	{
		var response = await factory.CreateClient().GetAsync($"/api/v1/tenants/{Guid.NewGuid()}/entra/group-role-mappings");

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}
}
