using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Leitura, exclusão e membros de perfil (issue #26).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class RoleProfileManagementTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"perfil-{Guid.NewGuid():N}@secco.test";

	[Fact]
	public async Task GetRole_Existente_DevolvePermissoesEContagemDeMembros()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "financeiro-user", "documentos:read", "boletos:read");
		await IdentitySeed.UserAsync(factory, _tenantId, Email(), "financeiro-user");
		await IdentitySeed.UserAsync(factory, _tenantId, Email(), "financeiro-user");

		var role = await IdentitySeed.AdminClient(factory)
			.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/roles/financeiro-user", Json);

		role.GetProperty("name").GetString().Should().Be("financeiro-user");
		role.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
			.Should().BeEquivalentTo("boletos:read", "documentos:read");
		role.GetProperty("isReserved").GetBoolean().Should().BeFalse();
		role.GetProperty("memberCount").GetInt32().Should().Be(2);
	}

	[Fact]
	public async Task GetRole_OperadorDaInstalacao_MarcaReservadoComReadSetDaPlataforma()
	{
		var role = await IdentitySeed.AdminClient(factory).GetFromJsonAsync<JsonElement>(
			$"/api/v1/tenants/{SecureGatePlatform.TenantId}/roles/{SecureGatePlatform.OperatorRole}", Json);

		role.GetProperty("isReserved").GetBoolean().Should().BeTrue();
		role.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
			.Should().BeEquivalentTo(SecureGatePlatform.OperatorReadPermissions);
	}

	[Fact]
	public async Task GetRole_DeOutroTenant_Retorna404()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, otherTenant, "so-no-outro");

		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/roles/so-no-outro");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task GetRole_NomeInvalido_Retorna400()
	{
		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/roles/nome%20com%20espaco");

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task ListRoleMembers_PaginaEMostraSituacao()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor");
		await IdentitySeed.UserAsync(factory, _tenantId, "a-" + Email(), "leitor");
		await IdentitySeed.UserAsync(factory, _tenantId, "b-" + Email(), "leitor");
		var deactivated = await IdentitySeed.UserAsync(factory, _tenantId, "c-" + Email(), "leitor");
		await IdentitySeed.DeactivateAsync(factory, deactivated);
		var admin = IdentitySeed.AdminClient(factory);

		var first = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/roles/leitor/members?page=1&size=2", Json);
		var second = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/roles/leitor/members?page=2&size=2", Json);

		first.GetProperty("totalCount").GetInt64().Should().Be(3);
		first.GetProperty("items").GetArrayLength().Should().Be(2);
		second.GetProperty("items").GetArrayLength().Should().Be(1);
		second.GetProperty("items")[0].GetProperty("userId").GetGuid().Should().Be(deactivated);
		second.GetProperty("items")[0].GetProperty("status").GetString().Should().Be("Deactivated");
	}

	[Fact]
	public async Task ListRoleMembers_PerfilDeOutroTenant_Retorna404()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, otherTenant, "alheio");

		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/roles/alheio/members");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
}
