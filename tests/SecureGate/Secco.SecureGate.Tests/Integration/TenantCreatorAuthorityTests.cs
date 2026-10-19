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

/// <summary>
/// Admin de tenant cria tenant e cuida do ciclo de vida dele (ADR-0047), ponta a ponta com o login real: a
/// concessão de criação é do operador, o tenant nasce administrado por quem criou e dentro de um limite, a
/// desativação não tranca conta administrativa, e o provisionamento é só automático e com nomes derivados.
/// </summary>
[Collection(TenantAdminApiCollectionDefinition.Name)]
public class TenantCreatorAuthorityTests(TenantAdminSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "tenant-creator-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string Scope = "openid offline_access securegate:tenant-admin";
	private const string MachineClientId = "tenant-creator-operator-machine";
	private const string MachineSecret = "tenant-creator-operator-machine-secret!";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private OidcLoginDriver Driver => new(secureGate, ClientId, RedirectUri, IdentitySeed.Password);

	private Guid _ownerTenant;
	private Guid _creator;
	private string _creatorEmail = string.Empty;

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, SecureGateScopes.TenantAdmin);
		await secureGate.CreateClientAsync(MachineClientId, MachineSecret, SecureGateScopes.Admin);
		secureGate.Auditor.Reset();

		_ownerTenant = await IdentitySeed.TenantAsync(secureGate);
		_creatorEmail = $"criador-{Guid.NewGuid():N}@secco.test";
		_creator = await IdentitySeed.UserAsync(secureGate, _ownerTenant, _creatorEmail);
		await IdentitySeed.EnableTwoFactorAsync(secureGate, _creator);
	}

	public Task DisposeAsync()
	{
		secureGate.Auditor.Reset();

		return Task.CompletedTask;
	}

	// ---- Criação ----

	[Fact]
	public async Task SoConcessaoDeCriacao_JaDaOEscopoNoLogin()
	{
		await GrantCreatorAsync(_creator);

		var (accessToken, _) = await Driver.LoginAsync(_creatorEmail, Scope);

		OidcLoginDriver.Scopes(accessToken).Should().Contain(SecureGateScopes.TenantAdmin);
	}

	[Fact]
	public async Task SemConcessaoDeCriacao_NaoCria_MesmoAdministrandoOutroTenant()
	{
		var outro = await IdentitySeed.TenantAsync(secureGate);
		await GrantAdminAsync(outro, _creator);
		using var admin = await TenantAdminClientAsync();

		var response = await admin.PostAsJsonAsync("/api/v1/tenants", NewTenant());

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		secureGate.Auditor.Records.Should().BeEmpty();
	}

	[Fact]
	public async Task Criacao_NasceAdministradaPorQuemCriou_EAuditadaNoTenantDonoDela()
	{
		await GrantCreatorAsync(_creator);
		using var criador = await TenantAdminClientAsync();

		var response = await criador.PostAsJsonAsync("/api/v1/tenants", NewTenant());

		response.StatusCode.Should().Be(HttpStatusCode.Created);
		var created = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

		(await criador.GetAsync($"/api/v1/tenants/{created}/users")).StatusCode
			.Should().Be(HttpStatusCode.OK, "o tenant nasce administrado por quem o criou");

		var administrados = await criador.GetFromJsonAsync<JsonElement>("/api/v1/tenant-admin/tenants", Json);
		administrados.EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).Should().Contain(created);

		var record = secureGate.Auditor.Records.Should().ContainSingle().Subject;
		record.Operation.Should().Be("CreateTenant");
		record.TenantId.Should().Be(_ownerTenant, "o tenant novo ainda não existe; a trilha é a do tenant dono de quem criou");
		record.AdministeredTenantId.Should().BeNull();
	}

	[Fact]
	public async Task ConcessaoAutomatica_CobreSoOTenantCriado()
	{
		await GrantCreatorAsync(_creator);
		var alheio = await IdentitySeed.TenantAsync(secureGate);
		using var criador = await TenantAdminClientAsync();

		(await criador.PostAsJsonAsync("/api/v1/tenants", NewTenant())).StatusCode.Should().Be(HttpStatusCode.Created);

		(await criador.GetAsync($"/api/v1/tenants/{alheio}/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await criador.GetAsync($"/api/v1/tenants/{_ownerTenant}/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Limite_DeTenantsCriadosPorPessoa_Responde409()
	{
		await GrantCreatorAsync(_creator);
		using var criador = await TenantAdminClientAsync();

		for (var i = 0; i < TenantAdminSecureGateApiFactory.MaxCreatedTenantsPerPerson; i++)
		{
			(await criador.PostAsJsonAsync("/api/v1/tenants", NewTenant())).StatusCode.Should().Be(HttpStatusCode.Created);
		}

		(await criador.PostAsJsonAsync("/api/v1/tenants", NewTenant())).StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task RevogarCriacao_ImpedeCriar_MasMantemOQueJaFoiCriado()
	{
		await GrantCreatorAsync(_creator);
		using var criador = await TenantAdminClientAsync();
		var response = await criador.PostAsJsonAsync("/api/v1/tenants", NewTenant());
		var created = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

		using (var operador = await OperatorAsync())
		{
			(await operador.DeleteAsync($"/api/v1/tenant-creators/{_creator}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
		}

		(await criador.PostAsJsonAsync("/api/v1/tenants", NewTenant())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await criador.GetAsync($"/api/v1/tenants/{created}/users")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task ConcessaoDeCriacao_ComAutoridadeDeAdminDeTenant_Responde403()
	{
		await GrantCreatorAsync(_creator);
		var colega = await IdentitySeed.UserAsync(secureGate, _ownerTenant, $"colega-{Guid.NewGuid():N}@secco.test");
		using var criador = await TenantAdminClientAsync();

		(await criador.PutAsync($"/api/v1/tenant-creators/{colega}", null)).StatusCode
			.Should().Be(HttpStatusCode.Forbidden, "só o operador concede criação");
	}

	[Fact]
	public async Task QuemSoPodeCriar_EContaAdministrativa_ProtegidaContraOAdminDoTenantDono()
	{
		var tenant = await IdentitySeed.TenantAsync(secureGate);
		await GrantAdminAsync(tenant, _creator);

		// Dona no tenant, só com a concessão de criação: o admin desse tenant não pode zerar o segundo fator dela
		var criadora = await IdentitySeed.UserAsync(secureGate, tenant, $"criadora-{Guid.NewGuid():N}@secco.test");
		await GrantCreatorAsync(criadora);
		using var admin = await TenantAdminClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{tenant}/users/{criadora}/two-factor/reset", null))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	// ---- Ciclo de vida ----

	[Fact]
	public async Task Desativar_TenantAdministradoSemContaAdministrativa_Passa_MasReativarESoDoOperador()
	{
		var tenant = await IdentitySeed.TenantAsync(secureGate);
		await GrantAdminAsync(tenant, _creator);
		using var admin = await TenantAdminClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{tenant}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		// Reativar desfaria uma suspensão feita pelo operador: fica com ele
		(await admin.PostAsync($"/api/v1/tenants/{tenant}/activate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Desativar_TenantDonoDeContaAdministrativa_Responde409()
	{
		var tenant = await IdentitySeed.TenantAsync(secureGate);
		await GrantAdminAsync(tenant, _creator);

		// Dona no tenant e admin de outro: desativar o tenant dono a trancaria em todos os sistemas (ADR-0040)
		var outraAdmin = await IdentitySeed.UserAsync(secureGate, tenant, $"admin-dona-{Guid.NewGuid():N}@secco.test");
		await GrantAdminAsync(await IdentitySeed.TenantAsync(secureGate), outraAdmin);
		using var admin = await TenantAdminClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{tenant}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Provisionamento_Automatico_ComNomesDerivados_Passa()
	{
		var tenant = await IdentitySeed.TenantAsync(secureGate);
		await GrantAdminAsync(tenant, _creator);
		using var admin = await TenantAdminClientAsync();

		var response = await admin.PostAsJsonAsync(
			$"/api/v1/tenants/{tenant}/databases/logstream/provisioning", new { createDatabase = true });

		response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
		var result = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
		result.GetProperty("applied").GetBoolean().Should().BeTrue();
		result.GetProperty("script").ValueKind.Should().Be(JsonValueKind.Null, "a senha nunca volta para o admin de tenant");
		(await admin.GetAsync($"/api/v1/tenants/{tenant}/databases/status")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Theory]
	[InlineData("{\"createDatabase\":false}")]
	[InlineData("{\"createDatabase\":true,\"databaseName\":\"banco_de_outro\"}")]
	[InlineData("{\"createDatabase\":true,\"loginName\":\"login_de_outro\"}")]
	[InlineData("{\"createDatabase\":true,\"server\":\"outro-servidor\"}")]
	[InlineData("{\"createDatabase\":true,\"target\":\"default\"}")]
	public async Task Provisionamento_ComNomesDoPedidoOuAssumindoDatabase_Responde403(string body)
	{
		var tenant = await IdentitySeed.TenantAsync(secureGate);
		await GrantAdminAsync(tenant, _creator);
		using var admin = await TenantAdminClientAsync();

		var response = await admin.PostAsync(
			$"/api/v1/tenants/{tenant}/databases/logstream/provisioning",
			new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "o admin de um tenant não escolhe nem assume o database de outro");
	}

	[Fact]
	public async Task Provisionamento_DeProdutoForaDaLista_Responde403()
	{
		var tenant = await IdentitySeed.TenantAsync(secureGate);
		await GrantAdminAsync(tenant, _creator);
		using var admin = await TenantAdminClientAsync();

		// Produto livre mudaria o nome derivado ("secco_<produto>_<slug>") até colidir com o banco de outro tenant
		var response = await admin.PostAsJsonAsync(
			$"/api/v1/tenants/{tenant}/databases/logstream-x/provisioning", new { createDatabase = true });

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Provisionamento_ComDatabaseJaExistente_NaoAssume()
	{
		var tenant = await IdentitySeed.TenantAsync(secureGate);
		await GrantAdminAsync(tenant, _creator);

		// O database com o nome derivado já existe (outro tenant, ou alguém criou à mão): o restrito não entra nele
		await CreateDatabaseAsync(await DerivedDatabaseNameAsync(tenant, "logstream"));
		using var admin = await TenantAdminClientAsync();

		var response = await admin.PostAsJsonAsync(
			$"/api/v1/tenants/{tenant}/databases/logstream/provisioning", new { createDatabase = true });

		response.IsSuccessStatusCode.Should().BeFalse("o provisionamento restrito só cria, nunca assume");
		(await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenant}", Json))
			.GetProperty("products").EnumerateArray().Should().BeEmpty("nada foi gravado no catálogo");
	}

	// ---- Apoio ----

	private async Task<string> DerivedDatabaseNameAsync(Guid tenantId, string product)
	{
		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var slug = (await context.Tenants.SingleAsync(t => t.Id == tenantId)).Slug;

		return Secco.SecureGate.Application.Provisioning.DatabaseIdentifierPolicy.DeriveForTenant(tenantId, "secco", product, slug);
	}

	private async Task CreateDatabaseAsync(string name)
	{
		var admin = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(secureGate.GetPlatformConnectionString())
		{
			InitialCatalog = "master",
		};
		await using var connection = new Microsoft.Data.SqlClient.SqlConnection(admin.ConnectionString);
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = $"CREATE DATABASE [{name}]";
		await command.ExecuteNonQueryAsync();
	}

	private static object NewTenant() => new { name = "Sistema criado", slug = $"t-{Guid.NewGuid():N}" };

	private async Task GrantCreatorAsync(Guid userId)
	{
		using var operador = await OperatorAsync();

		(await operador.PutAsync($"/api/v1/tenant-creators/{userId}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	private async Task GrantAdminAsync(Guid tenantId, Guid userId)
	{
		using var operador = await OperatorAsync();

		(await operador.PutAsync($"/api/v1/tenants/{tenantId}/admins/{userId}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	private async Task<HttpClient> TenantAdminClientAsync()
	{
		var (accessToken, _) = await Driver.LoginAsync(_creatorEmail, Scope);
		OidcLoginDriver.Scopes(accessToken).Should().Contain(SecureGateScopes.TenantAdmin);

		return Driver.BearerClient(accessToken);
	}

	private async Task<HttpClient> OperatorAsync()
	{
		using var client = secureGate.CreateClient();
		var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = MachineClientId,
			["client_secret"] = MachineSecret,
			["scope"] = SecureGateScopes.Admin,
		}));
		response.EnsureSuccessStatusCode();

		using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		var operador = secureGate.CreateClient();
		operador.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", payload.RootElement.GetProperty("access_token").GetString());

		return operador;
	}
}
