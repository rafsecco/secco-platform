using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// ADR-0048, ponta a ponta com o login real: o admin de tenant lê o catálogo de permissões e gere os clients de
/// MÁQUINA dos tenants que administra; client de login (interativo) continua com o operador, porque quem controla a
/// redirect URI recebe o code de qualquer membro do tenant.
/// </summary>
[Collection(TenantAdminApiCollectionDefinition.Name)]
public class TenantAdminClientsAndCatalogTests(TenantAdminSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "tenant-admin-clients-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string Scope = "openid offline_access securegate:tenant-admin";
	private const string MachineClientId = "tenant-admin-clients-operator";
	private const string MachineSecret = "tenant-admin-clients-operator-secret!!";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private OidcLoginDriver Driver => new(secureGate, ClientId, RedirectUri, IdentitySeed.Password);

	private Guid _administered;
	private Guid _other;
	private Guid _admin;
	private string _adminEmail = string.Empty;

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, SecureGateScopes.TenantAdmin);
		await secureGate.CreateClientAsync(MachineClientId, MachineSecret, SecureGateScopes.Admin);
		secureGate.Auditor.Reset();

		var owner = await IdentitySeed.TenantAsync(secureGate);
		_administered = await IdentitySeed.TenantAsync(secureGate);
		_other = await IdentitySeed.TenantAsync(secureGate);
		_adminEmail = $"ta-clients-{Guid.NewGuid():N}@secco.test";
		_admin = await IdentitySeed.UserAsync(secureGate, owner, _adminEmail);
		await IdentitySeed.EnableTwoFactorAsync(secureGate, _admin);

		using var operador = await OperatorAsync();
		(await operador.PutAsync($"/api/v1/tenants/{_administered}/admins/{_admin}", null))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	public Task DisposeAsync()
	{
		secureGate.Auditor.Reset();

		return Task.CompletedTask;
	}

	// ---- Catálogo (ADR-0048) ----

	[Fact]
	public async Task Catalogo_ComAlgumaConcessao_Le()
	{
		using var admin = await TenantAdminClientAsync();

		(await admin.GetAsync("/api/v1/permission-catalog")).StatusCode.Should().Be(HttpStatusCode.OK);
		secureGate.Auditor.Records.Should().BeEmpty("leitura não vai à trilha");
	}

	[Fact]
	public async Task Catalogo_ConcessaoRevogada_ComOMesmoToken_Recusa()
	{
		using var admin = await TenantAdminClientAsync();

		using (var operador = await OperatorAsync())
		{
			(await operador.DeleteAsync($"/api/v1/tenants/{_administered}/admins/{_admin}"))
				.StatusCode.Should().Be(HttpStatusCode.NoContent);
		}

		(await admin.GetAsync("/api/v1/permission-catalog")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Catalogo_PublicarContinuaSoDoProduto()
	{
		using var admin = await TenantAdminClientAsync();

		var response = await admin.PutAsJsonAsync("/api/v1/permission-catalog/logstream", new { permissions = Array.Empty<object>() });

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	// ---- Clients de máquina (ADR-0048) ----

	[Fact]
	public async Task ClientDeMaquina_CriaRotacionaERevoga_NoTenantAdministrado_ComTrilha()
	{
		using var admin = await TenantAdminClientAsync();

		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{_administered}/clients", MachineClient("intranet-logs"));
		created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
		var clientId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("clientId").GetString();

		(await admin.PostAsync($"/api/v1/tenants/{_administered}/clients/{clientId}/rotate-secret", null))
			.StatusCode.Should().Be(HttpStatusCode.OK);
		(await admin.PutAsJsonAsync($"/api/v1/tenants/{_administered}/clients/{clientId}", MachineClient("intranet-logs-2")))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await admin.DeleteAsync($"/api/v1/tenants/{_administered}/clients/{clientId}"))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		secureGate.Auditor.Records.Select(r => r.Operation).Should().Equal(
			"CreateProductClient", "RotateProductClientSecret", "UpdateProductClient", "DeleteProductClient");
	}

	[Fact]
	public async Task ClientDeMaquina_EmTenantNaoAdministrado_Recusa()
	{
		using var admin = await TenantAdminClientAsync();

		(await admin.PostAsJsonAsync($"/api/v1/tenants/{_other}/clients", MachineClient("intruso")))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await admin.GetAsync($"/api/v1/tenants/{_other}/clients")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	// ---- Clients de login ficam com o operador (ADR-0048) ----

	[Fact]
	public async Task ClientDeLogin_CriarPelaAutoridade_Responde403()
	{
		using var admin = await TenantAdminClientAsync();

		var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{_administered}/clients", InteractiveClient("sso-desviado"));

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "quem controla a redirect URI recebe o code dos membros do tenant");
	}

	[Fact]
	public async Task ClientDeLogin_DoOperador_ListaMasNaoAltera()
	{
		string clientId;

		using (var operador = await OperatorAsync())
		{
			var created = await operador.PostAsJsonAsync($"/api/v1/tenants/{_administered}/clients", InteractiveClient("sso-compras"));
			created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
			clientId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("clientId").GetString()!;
		}

		using var admin = await TenantAdminClientAsync();
		var route = $"/api/v1/tenants/{_administered}/clients/{clientId}";

		(await admin.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.OK);
		(await admin.PutAsJsonAsync(route, InteractiveClient("sso-compras", "https://desviado.test/cb")))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden, "trocar a redirect URI desviaria o code");
		(await admin.PostAsync(route + "/rotate-secret", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await admin.DeleteAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	// ---- Apoio ----

	private static object MachineClient(string name) =>
		new { name, type = "client_credentials", scopes = new[] { "logstream" } };

	private static object InteractiveClient(string name, string redirect = "https://compras.test/signin-oidc") =>
		new { name, type = "authorization_code", scopes = new[] { "logstream" }, redirectUris = new[] { redirect } };

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
}
