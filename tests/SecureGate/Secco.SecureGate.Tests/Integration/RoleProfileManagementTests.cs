using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
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

	[Fact]
	public async Task DeleteRole_SemMembros_ExcluiPerfilEPermissoes()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "temporario", "relatorios:read");
		var admin = IdentitySeed.AdminClient(factory);

		(await admin.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/temporario")).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await admin.GetAsync($"/api/v1/tenants/{_tenantId}/roles/temporario")).StatusCode.Should().Be(HttpStatusCode.NotFound);

		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.RoleClaims.CountAsync(c => c.ClaimValue == "relatorios:read"
			&& !context.Roles.Any(r => r.Id == c.RoleId))).Should().Be(0, "permissões órfãs não podem sobrar");
	}

	[Fact]
	public async Task DeleteRole_ComMembros_Retorna409EMantem()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "ocupado");
		await IdentitySeed.UserAsync(factory, _tenantId, Email(), "ocupado");
		var admin = IdentitySeed.AdminClient(factory);

		(await admin.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/ocupado")).StatusCode.Should().Be(HttpStatusCode.Conflict);

		(await admin.GetAsync($"/api/v1/tenants/{_tenantId}/roles/ocupado")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task DeleteRole_OperadorDaInstalacao_Retorna409()
	{
		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/roles/{SecureGatePlatform.OperatorRole}");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task DeleteRole_Inexistente_Retorna404()
	{
		var response = await IdentitySeed.AdminClient(factory).DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/nao-existe");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task DeleteRole_DeOutroTenant_Retorna404ENaoExclui()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, otherTenant, "do-vizinho");
		var admin = IdentitySeed.AdminClient(factory);

		(await admin.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/do-vizinho")).StatusCode.Should().Be(HttpStatusCode.NotFound);

		(await admin.GetAsync($"/api/v1/tenants/{otherTenant}/roles/do-vizinho")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	public static TheoryData<string, string> NovasRotas() => new()
	{
		{ "GET", "/api/v1/tenants/{0}/roles/leitor" },
		{ "DELETE", "/api/v1/tenants/{0}/roles/leitor" },
		{ "GET", "/api/v1/tenants/{0}/roles/leitor/members" },
		{ "GET", "/api/v1/tenants/{0}/users/{1}" },
		{ "POST", "/api/v1/tenants/{0}/users/{1}/roles/leitor" },
		{ "DELETE", "/api/v1/tenants/{0}/users/{1}/roles/leitor" },
	};

	[Theory]
	[MemberData(nameof(NovasRotas))]
	public async Task NovasRotas_SemToken_Retornam401(string method, string template)
	{
		using var request = new HttpRequestMessage(
			new HttpMethod(method), string.Format(CultureInfo.InvariantCulture, template, _tenantId, Guid.CreateVersion7()));

		(await factory.CreateClient().SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Theory]
	[MemberData(nameof(NovasRotas))]
	public async Task NovasRotas_SemScopeAdmin_Retornam403(string method, string template)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes("logstream"));
		using var request = new HttpRequestMessage(
			new HttpMethod(method), string.Format(CultureInfo.InvariantCulture, template, _tenantId, Guid.CreateVersion7()));

		(await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task DeleteRole_UsadoPorClientDoTenant_409()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "maquina-writer", "log-entries:write");
		await factory.CreateProductClientAsync(_tenantId, $"cli_{Guid.NewGuid():N}"[..20],
			"client-secret-de-32-chars-minimo!!!", "outro Maquina-Writer", "logstream");

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/maquina-writer");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await response.Content.ReadAsStringAsync()).Should().Contain("SecureGate.Role.UsedByClients");
	}

	[Fact]
	public async Task DeleteRole_ClientComPapelDeNomeParecido_NaoBloqueia()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "tenant-admin-x");
		await factory.CreateProductClientAsync(_tenantId, $"cli_{Guid.NewGuid():N}"[..20],
			"client-secret-de-32-chars-minimo!!!", "admin-x", "logstream");

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/tenant-admin-x");

		response.StatusCode.Should().Be(HttpStatusCode.NoContent, "comparação por papel inteiro, não por substring");
	}

	[Fact]
	public async Task DeleteRole_PapelQueEhSubstringDoPapelDoClient_NaoBloqueia()
	{
		// Direção oposta do teste anterior: o perfil excluído é o CURTO ("admin-z") e o client guarda
		// papéis que o CONTÊM ("tenant-admin-z", "admin-z-ops"). Comparar por substring bloquearia à toa.
		await IdentitySeed.RoleAsync(factory, _tenantId, "admin-z");
		await factory.CreateProductClientAsync(_tenantId, $"cli_{Guid.NewGuid():N}"[..20],
			"client-secret-de-32-chars-minimo!!!", "tenant-admin-z admin-z-ops", "logstream");

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/admin-z");

		response.StatusCode.Should().Be(HttpStatusCode.NoContent, "comparação por papel inteiro, não por substring");
	}

	[Fact]
	public async Task DeleteRole_ClientDeOutroTenantComMesmoNome_NaoBloqueia()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "homonimo-y");
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await factory.CreateProductClientAsync(otherTenant, $"cli_{Guid.NewGuid():N}"[..20],
			"client-secret-de-32-chars-minimo!!!", "homonimo-y", "logstream");

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/homonimo-y");

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}
}
