using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Admin de tenant (ADR-0044), ponta a ponta com o login real: a concessão é do operador, o escopo só
/// sai com concessão e segundo fator, a rota precisa estar marcada, a concessão é conferida a cada
/// requisição e toda escrita é auditada antes de acontecer, no tenant administrado.
/// </summary>
[Collection(TenantAdminApiCollectionDefinition.Name)]
public class TenantAdminAuthorityTests(TenantAdminSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "tenant-admin-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string Scope = "openid offline_access securegate:tenant-admin";
	private const string MachineClientId = "tenant-admin-operator-machine";
	private const string MachineSecret = "tenant-admin-operator-machine-secret!";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private OidcLoginDriver Driver => new(secureGate, ClientId, RedirectUri, IdentitySeed.Password);

	// Cenário: a pessoa é dona em A (a "Intranet") e administra B (o "Compras"); C ela não administra
	private Guid _tenantA;
	private Guid _tenantB;
	private Guid _tenantC;
	private Guid _admin;
	private string _adminEmail = string.Empty;

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, SecureGateScopes.TenantAdmin);
		await secureGate.CreateClientAsync(MachineClientId, MachineSecret, SecureGateScopes.Admin);
		secureGate.Auditor.Reset();

		_tenantA = await IdentitySeed.TenantAsync(secureGate);
		_tenantB = await IdentitySeed.TenantAsync(secureGate);
		_tenantC = await IdentitySeed.TenantAsync(secureGate);
		_adminEmail = $"ta-{Guid.NewGuid():N}@secco.test";
		_admin = await IdentitySeed.UserAsync(secureGate, _tenantA, _adminEmail);
		await IdentitySeed.EnableTwoFactorAsync(secureGate, _admin);
	}

	public Task DisposeAsync()
	{
		secureGate.Auditor.Reset();

		return Task.CompletedTask;
	}

	// ---- Gestão da concessão (só operador) ----

	[Fact]
	public async Task Grant_NoTenantDePlataforma_Responde409()
	{
		using var operador = await OperatorAsync();

		var response = await operador.PutAsync(AdminsRoute(SecureGatePlatform.TenantId, _admin), null);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict, "autoridade de tenant sobre a plataforma seria autoridade de instalação");
	}

	[Fact]
	public async Task Grant_DuasVezes_EIdempotente_EAListaMostraUmaVezComOTenantDono()
	{
		using var operador = await OperatorAsync();

		(await operador.PutAsync(AdminsRoute(_tenantB, _admin), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await operador.PutAsync(AdminsRoute(_tenantB, _admin), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var admins = await operador.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantB}/admins", Json);

		admins.GetArrayLength().Should().Be(1);
		admins[0].GetProperty("userId").GetGuid().Should().Be(_admin);
		admins[0].GetProperty("email").GetString().Should().Be(_adminEmail);
		admins[0].GetProperty("ownerTenantId").GetGuid().Should().Be(_tenantA, "a pessoa não precisa ser do tenant que administra");
	}

	[Fact]
	public async Task Grant_PessoaInexistente_Responde404()
	{
		using var operador = await OperatorAsync();

		(await operador.PutAsync(AdminsRoute(_tenantB, Guid.NewGuid()), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Grant_ComAutoridadeDeAdminDeTenant_Responde403()
	{
		await GrantAsync(_tenantB, _admin);
		var colega = await IdentitySeed.UserAsync(secureGate, _tenantB, $"colega-{Guid.NewGuid():N}@secco.test");
		using var admin = await TenantAdminClientAsync();

		(await admin.PutAsync(AdminsRoute(_tenantB, colega), null)).StatusCode
			.Should().Be(HttpStatusCode.Forbidden, "admin de tenant não nomeia outro admin");
	}

	// ---- Emissão do escopo ----

	[Fact]
	public async Task Login_ComConcessaoESegundoFator_RecebeOEscopo_EListaSoOsTenantsAdministrados()
	{
		await GrantAsync(_tenantB, _admin);

		var (accessToken, _) = await Driver.LoginAsync(_adminEmail, Scope);

		OidcLoginDriver.Scopes(accessToken).Should().Contain(SecureGateScopes.TenantAdmin);

		using var admin = Driver.BearerClient(accessToken);
		var tenants = await admin.GetFromJsonAsync<JsonElement>("/api/v1/tenant-admin/tenants", Json);

		tenants.EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).Should().Equal(_tenantB);
	}

	[Fact]
	public async Task Login_SemConcessao_NaoRecebeOEscopo()
	{
		var (accessToken, _) = await Driver.LoginAsync(_adminEmail, Scope);

		OidcLoginDriver.Scopes(accessToken).Should().NotContain(SecureGateScopes.TenantAdmin);
	}

	[Fact]
	public async Task Login_ComConcessaoSemSegundoFator_VaiAoCadastroAntesDoCode()
	{
		var semFator = $"ta-sem-fator-{Guid.NewGuid():N}@secco.test";
		var pessoa = await IdentitySeed.UserAsync(secureGate, _tenantA, semFator);
		await GrantAsync(_tenantB, pessoa);

		using var browser = Driver.CreateBrowser();
		var (_, challenge) = OidcLoginDriver.CreatePkce();
		var response = await Driver.SubmitLoginAsync(browser, semFator, Scope, challenge);
		var authorize = await browser.GetAsync(response.Headers.Location!.ToString());

		authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
		authorize.Headers.Location!.ToString().Should().StartWith("/conta/dois-fatores", "sem segundo fator não há code");
	}

	[Fact]
	public async Task Login_SemAuditoriaConfigurada_NaoRecebeOEscopo()
	{
		await GrantAsync(_tenantB, _admin);
		secureGate.Auditor.IsConfigured = false;

		var (accessToken, _) = await Driver.LoginAsync(_adminEmail, Scope);

		OidcLoginDriver.Scopes(accessToken).Should().NotContain(SecureGateScopes.TenantAdmin, "sem trilha, sem autoridade");
	}

	[Fact]
	public async Task Renovacao_DepoisDaRevogacao_PerdeOEscopo()
	{
		await GrantAsync(_tenantB, _admin);
		var (_, refreshToken) = await Driver.LoginAsync(_adminEmail, Scope);

		using (var operador = await OperatorAsync())
		{
			(await operador.DeleteAsync(AdminsRoute(_tenantB, _admin))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		}

		var refreshed = await Driver.RefreshAsync(refreshToken);
		refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
		using var payload = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());

		OidcLoginDriver.Scopes(payload.RootElement.GetProperty("access_token").GetString()!)
			.Should().NotContain(SecureGateScopes.TenantAdmin);
	}

	// ---- Conferência a cada requisição ----

	[Fact]
	public async Task Rota_DoTenantAdministrado_Passa_DeOutroTenant_EDaPlataforma_Recusam()
	{
		await GrantAsync(_tenantB, _admin);
		using var admin = await TenantAdminClientAsync();

		(await admin.GetAsync($"/api/v1/tenants/{_tenantB}/users")).StatusCode.Should().Be(HttpStatusCode.OK);
		(await admin.GetAsync($"/api/v1/tenants/{_tenantB}")).StatusCode.Should().Be(HttpStatusCode.OK);
		(await admin.GetAsync($"/api/v1/tenants/{_tenantC}/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await admin.GetAsync($"/api/v1/tenants/{_tenantA}/users")).StatusCode
			.Should().Be(HttpStatusCode.Forbidden, "ser dono da conta em A não dá autoridade sobre A");
		(await admin.GetAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Theory]
	[InlineData("GET", "/api/v1/tenants")]
	[InlineData("PUT", "/api/v1/tenants/{b}/databases/logstream")]
	[InlineData("PUT", "/api/v1/tenants/{b}/federation")]
	[InlineData("GET", "/api/v1/tenants/{b}/clients")]
	[InlineData("GET", "/api/v1/tenants/{b}/entra/group-role-mappings")]
	[InlineData("GET", "/api/v1/tenants/{b}/admins")]
	[InlineData("GET", "/api/v1/permission-catalog")]
	public async Task RotaNaoMarcada_RecusaAAutoridade_MesmoNoTenantAdministrado(string method, string route)
	{
		await GrantAsync(_tenantB, _admin);
		using var admin = await TenantAdminClientAsync();

		var response = await admin.SendAsync(new HttpRequestMessage(new HttpMethod(method), route.Replace("{b}", _tenantB.ToString(), StringComparison.Ordinal)));

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		secureGate.Auditor.Records.Should().BeEmpty("rota recusada não chega à trilha");
	}

	[Fact]
	public async Task Revogacao_ValeNaRequisicaoSeguinte_ComOMesmoToken()
	{
		await GrantAsync(_tenantB, _admin);
		using var admin = await TenantAdminClientAsync();
		(await admin.GetAsync($"/api/v1/tenants/{_tenantB}/users")).StatusCode.Should().Be(HttpStatusCode.OK);

		using (var operador = await OperatorAsync())
		{
			(await operador.DeleteAsync(AdminsRoute(_tenantB, _admin))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		}

		(await admin.GetAsync($"/api/v1/tenants/{_tenantB}/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task ContaDesativada_ComTokenAindaValido_Recusa()
	{
		await GrantAsync(_tenantB, _admin);
		using var admin = await TenantAdminClientAsync();

		await IdentitySeed.DeactivateAsync(secureGate, _admin);

		(await admin.GetAsync($"/api/v1/tenants/{_tenantB}/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	// ---- Trilha ----

	[Fact]
	public async Task Escrita_EAuditadaNoTenantAdministrado_QuandoEleTemLogStream()
	{
		await GrantAsync(_tenantB, _admin);
		using (var operador = await OperatorAsync())
		{
			(await operador.PutAsJsonAsync($"/api/v1/tenants/{_tenantB}/databases/logstream",
				new { connectionString = "Server=logstream-b;Database=logs;User Id=app;Password=x" })).IsSuccessStatusCode.Should().BeTrue();
		}

		using var admin = await TenantAdminClientAsync();

		var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantB}/roles", new { name = "compras-aprovador" });

		response.StatusCode.Should().Be(HttpStatusCode.Created);
		var record = secureGate.Auditor.Records.Should().ContainSingle().Subject;
		record.UserId.Should().Be(_admin);
		record.TenantId.Should().Be(_tenantB, "a trilha fica no tenant administrado quando ele tem LogStream");
		record.AdministeredTenantId.Should().Be(_tenantB);
		record.Operation.Should().Be("CreateRole");
		record.ClientId.Should().Be(ClientId, "a aplicação é o act do pedido");
	}

	[Fact]
	public async Task Escrita_EmTenantSemLogStream_VaiParaATrilhaDoTenantDonoDeQuemAgiu()
	{
		await GrantAsync(_tenantB, _admin);
		using var admin = await TenantAdminClientAsync();

		(await admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantB}/roles", new { name = "sem-logstream" }))
			.StatusCode.Should().Be(HttpStatusCode.Created);

		var record = secureGate.Auditor.Records.Should().ContainSingle().Subject;
		record.TenantId.Should().Be(_tenantA, "sem banco do LogStream, o tenant administrado não recebe a entrada (ADR-0047)");
		record.AdministeredTenantId.Should().Be(_tenantB);
	}

	[Fact]
	public async Task Escrita_ComTrilhaRecusada_Responde503_ENaoAcontece()
	{
		await GrantAsync(_tenantB, _admin);
		using var admin = await TenantAdminClientAsync();
		secureGate.Auditor.Accept = false;

		var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantB}/roles", new { name = "nao-auditado" });

		response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
		(await RoleExistsAsync(_tenantB, "nao-auditado")).Should().BeFalse("escrita sem registro não acontece");
	}

	[Fact]
	public async Task Leitura_NaoVaiATrilha()
	{
		await GrantAsync(_tenantB, _admin);
		using var admin = await TenantAdminClientAsync();

		(await admin.GetAsync($"/api/v1/tenants/{_tenantB}/roles")).StatusCode.Should().Be(HttpStatusCode.OK);

		secureGate.Auditor.Records.Should().BeEmpty();
	}

	[Fact]
	public async Task Operador_NaoPassaPorEstaTrilha()
	{
		using var operador = await OperatorAsync();

		(await operador.PostAsJsonAsync($"/api/v1/tenants/{_tenantB}/roles", new { name = "do-operador" }))
			.StatusCode.Should().Be(HttpStatusCode.Created);

		secureGate.Auditor.Records.Should().BeEmpty("a ADR-0044 não muda nada para securegate:admin");
	}

	// ---- Operações de pessoa ----

	[Fact]
	public async Task OperacaoDePessoa_SobreOutroAdminDeTenant_Responde403()
	{
		await GrantAsync(_tenantB, _admin);

		// Dona em B e admin de C: o admin só de B não pode enfraquecer a conta dela
		var outraAdmin = await IdentitySeed.UserAsync(secureGate, _tenantB, $"outra-admin-{Guid.NewGuid():N}@secco.test");
		await GrantAsync(_tenantC, outraAdmin);
		using var admin = await TenantAdminClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{_tenantB}/users/{outraAdmin}/two-factor/reset", null))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await admin.PostAsync($"/api/v1/tenants/{_tenantB}/users/{outraAdmin}/deactivate", null))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		secureGate.Auditor.Records.Should().BeEmpty();
	}

	[Fact]
	public async Task OperacaoDePessoa_SobrePessoaComumDoTenant_Passa()
	{
		await GrantAsync(_tenantB, _admin);
		var comum = await IdentitySeed.UserAsync(secureGate, _tenantB, $"comum-{Guid.NewGuid():N}@secco.test");
		using var admin = await TenantAdminClientAsync();

		(await admin.PutAsJsonAsync($"/api/v1/tenants/{_tenantB}/users/{comum}/display-name", new { displayName = "Pessoa Comum" }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task OperacaoDePessoa_SobreMembroDonoDeOutroTenant_Responde404()
	{
		await GrantAsync(_tenantB, _admin);

		// Dona em C e só membro de B: operação de pessoa é do tenant dono (ADR-0040)
		var membro = await IdentitySeed.UserAsync(secureGate, _tenantC, $"membro-{Guid.NewGuid():N}@secco.test");
		using (var operador = await OperatorAsync())
		{
			(await operador.PutAsync($"/api/v1/tenants/{_tenantB}/members/{membro}", null)).IsSuccessStatusCode.Should().BeTrue();
		}

		using var admin = await TenantAdminClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{_tenantB}/users/{membro}/deactivate", null))
			.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task RevogarSessoesDoOperador_ComoMembroDoTenant_Responde403()
	{
		await GrantAsync(_tenantB, _admin);
		await IdentitySeed.RoleAsync(secureGate, _tenantB, "compras-leitor");

		// O operador incluído como membro de B: suspender o vínculo ou tirar um perfil moveria o stamp dele
		// e derrubaria as sessões dele em todas as aplicações (ADR-0032) — em ciclo, se fosse permitido
		var operadorPessoa = await IdentitySeed.PlatformOperatorAsync(secureGate, $"op-alvo-{Guid.NewGuid():N}@secco.test");
		using (var operador = await OperatorAsync())
		{
			(await operador.PutAsync($"/api/v1/tenants/{_tenantB}/members/{operadorPessoa}", null)).IsSuccessStatusCode.Should().BeTrue();
			(await operador.PostAsync($"/api/v1/tenants/{_tenantB}/users/{operadorPessoa}/roles/compras-leitor", null)).IsSuccessStatusCode.Should().BeTrue();
		}

		using var admin = await TenantAdminClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{_tenantB}/members/{operadorPessoa}/suspend", null))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await admin.DeleteAsync($"/api/v1/tenants/{_tenantB}/members/{operadorPessoa}"))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await admin.DeleteAsync($"/api/v1/tenants/{_tenantB}/users/{operadorPessoa}/roles/compras-leitor"))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		secureGate.Auditor.Records.Should().BeEmpty();
	}

	[Fact]
	public async Task SuspenderMembroComum_Passa()
	{
		await GrantAsync(_tenantB, _admin);
		var membro = await IdentitySeed.UserAsync(secureGate, _tenantC, $"membro-comum-{Guid.NewGuid():N}@secco.test");
		using (var operador = await OperatorAsync())
		{
			(await operador.PutAsync($"/api/v1/tenants/{_tenantB}/members/{membro}", null)).IsSuccessStatusCode.Should().BeTrue();
		}

		using var admin = await TenantAdminClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{_tenantB}/members/{membro}/suspend", null))
			.IsSuccessStatusCode.Should().BeTrue();
	}

	// ---- Apoio ----

	private static string AdminsRoute(Guid tenantId, Guid userId) => $"/api/v1/tenants/{tenantId}/admins/{userId}";

	private async Task GrantAsync(Guid tenantId, Guid userId)
	{
		using var operador = await OperatorAsync();

		(await operador.PutAsync(AdminsRoute(tenantId, userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	private async Task<HttpClient> TenantAdminClientAsync()
	{
		var (accessToken, _) = await Driver.LoginAsync(_adminEmail, Scope);
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

	private async Task<bool> RoleExistsAsync(Guid tenantId, string name)
	{
		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var normalized = name.ToUpperInvariant();

		return await context.Roles.AnyAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized);
	}
}
