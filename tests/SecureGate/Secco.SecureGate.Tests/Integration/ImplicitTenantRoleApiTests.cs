using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Perfil implícito do tenant pela API (ADR-0056): nasce com o tenant, não existe no tenant de plataforma, tem o
/// nome reservado, não se atribui nem se exclui, a lista de membros volta vazia, o admin edita as permissões e o
/// resolvedor devolve essas permissões só para o tenant certo.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class ImplicitTenantRoleApiTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private const string Implicit = SecureGatePlatform.TenantMemberRole;
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private HttpClient _admin = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await IdentitySeed.PermissionCatalogAsync(factory, "implicit-tests", "documentos:read", "manuais:read");
		_admin = IdentitySeed.AdminClient(factory);
	}

	public Task DisposeAsync()
	{
		_admin.Dispose();

		return Task.CompletedTask;
	}

	private async Task<Guid> CreateTenantAsync()
	{
		var created = await _admin.PostAsJsonAsync("/api/v1/tenants", new { name = "Tenant implícito", slug = $"t-{Guid.NewGuid():N}" });
		created.StatusCode.Should().Be(HttpStatusCode.Created);

		return (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
	}

	[Fact]
	public async Task TenantCriadoPelaApi_NasceComOPerfilImplicito()
	{
		var tenantId = await CreateTenantAsync();

		var roles = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/roles", Json);
		var implicitRole = roles.EnumerateArray().Single(r => r.GetProperty("name").GetString() == Implicit);
		implicitRole.GetProperty("isImplicit").GetBoolean().Should().BeTrue();
		implicitRole.GetProperty("permissions").GetArrayLength().Should().Be(0);

		var detail = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/roles/{Implicit}", Json);
		detail.GetProperty("isImplicit").GetBoolean().Should().BeTrue();
		detail.GetProperty("isReserved").GetBoolean().Should().BeFalse("o admin edita o implícito");
		detail.GetProperty("memberCount").GetInt32().Should().Be(0);
	}

	[Fact]
	public async Task PerfilComum_NaoEhImplicito()
	{
		var tenantId = await CreateTenantAsync();
		await IdentitySeed.RoleAsync(factory, tenantId, "leitor");

		var detail = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/roles/leitor", Json);

		detail.GetProperty("isImplicit").GetBoolean().Should().BeFalse();
	}

	[Fact]
	public async Task TenantDePlataforma_NaoTemOPerfilImplicito()
	{
		(await _admin.GetAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/roles/{Implicit}"))
			.StatusCode.Should().Be(HttpStatusCode.NotFound);

		var roles = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{SecureGatePlatform.TenantId}/roles", Json);
		roles.EnumerateArray().Select(r => r.GetProperty("name").GetString()).Should().NotContain(Implicit);
	}

	[Fact]
	public async Task CriarPerfilComONomeReservado_Retorna400()
	{
		var tenantId = await CreateTenantAsync();

		var response = await _admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/roles", new { name = "Tenant-Member" });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task AtribuirOImplicito_Retorna400()
	{
		var tenantId = await CreateTenantAsync();
		var userId = await IdentitySeed.UserAsync(factory, tenantId, $"membro-{Guid.NewGuid():N}@secco.test");

		var response = await _admin.PostAsync($"/api/v1/tenants/{tenantId}/users/{userId}/roles/{Implicit}", null);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task ExcluirOImplicito_Retorna409_EOPerfilContinua()
	{
		var tenantId = await CreateTenantAsync();

		(await _admin.DeleteAsync($"/api/v1/tenants/{tenantId}/roles/{Implicit}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await _admin.GetAsync($"/api/v1/tenants/{tenantId}/roles/{Implicit}")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task ListarMembrosDoImplicito_VoltaPaginaVazia_MesmoComPessoasNoTenant()
	{
		var tenantId = await CreateTenantAsync();
		await IdentitySeed.UserAsync(factory, tenantId, $"dono-{Guid.NewGuid():N}@secco.test");

		var members = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/roles/{Implicit}/members", Json);

		members.GetProperty("items").GetArrayLength().Should().Be(0);
		members.GetProperty("totalCount").GetInt64().Should().Be(0);
	}

	[Fact]
	public async Task ClienteDeMaquina_NaoPodeReceberOImplicito()
	{
		var tenantId = await CreateTenantAsync();

		var response = await _admin.PostAsJsonAsync(
			$"/api/v1/tenants/{tenantId}/clients",
			new { name = $"Maquina {Guid.NewGuid():N}", scopes = new[] { "logstream" }, roles = new[] { Implicit } });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task SetPermissions_AceitaOImplicito_ESegueRecusandoPerfisDeInstalacao()
	{
		var tenantId = await CreateTenantAsync();

		(await _admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/roles/{Implicit}/permissions", new { permissions = new[] { "documentos:read" } }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var detail = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/roles/{Implicit}", Json);
		detail.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Equal("documentos:read");

		foreach (var installationRole in new[]
		{
			SecureGatePlatform.OperatorRole, SecureGatePlatform.LegacyOperatorRole,
			SecureGatePlatform.ElevatedLogReaderRole, SecureGatePlatform.AuditorRole,
		})
		{
			(await _admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/roles/{installationRole}/permissions", new { permissions = new[] { "documentos:read" } }))
				.StatusCode.Should().Be(HttpStatusCode.BadRequest, installationRole);
		}
	}

	[Fact]
	public async Task SetPermissions_ContinuaValidandoOCatalogo_NoImplicito()
	{
		var tenantId = await CreateTenantAsync();

		(await _admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/roles/{Implicit}/permissions", new { permissions = new[] { "nao-declarada:read" } }))
			.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Resolvedor_DevolveAsPermissoesDoImplicito_SoParaOTenantCerto()
	{
		var tenantA = await CreateTenantAsync();
		var tenantB = await CreateTenantAsync();
		(await _admin.PutAsJsonAsync($"/api/v1/tenants/{tenantA}/roles/{Implicit}/permissions", new { permissions = new[] { "manuais:read" } }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		using var reader = factory.CreateClient();
		reader.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.AuthorizationRead));

		var inA = await reader.GetFromJsonAsync<string[]>($"/api/v1/authorization/tenants/{tenantA}/roles/{Implicit}/permissions", Json);
		var inB = await reader.GetFromJsonAsync<string[]>($"/api/v1/authorization/tenants/{tenantB}/roles/{Implicit}/permissions", Json);

		inA.Should().Equal("manuais:read");
		inB.Should().BeEmpty("o perfil implícito de um tenant não vaza para outro");
	}
}
