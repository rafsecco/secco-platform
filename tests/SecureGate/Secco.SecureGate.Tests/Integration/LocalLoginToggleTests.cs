using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Operações do admin sobre a credencial (ADR-0033): disparar redefinição e ligar/desligar o
/// login local. Em nenhuma delas o admin escolhe ou vê uma senha.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class LocalLoginToggleTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string ClientId = "local-login-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string AdminClientId = "local-login-admin";
	private const string AdminSecret = "local-login-admin-secret-32-chars-min!";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");
		await factory.CreateClientAsync(AdminClientId, AdminSecret, "securegate:admin");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"toggle-{Guid.NewGuid():N}@secco.test";

	private async Task<HttpClient> AdminAsync()
	{
		using var anonymous = factory.CreateClient();
		var response = await anonymous.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = AdminClientId,
			["client_secret"] = AdminSecret,
			["scope"] = "securegate:admin",
		}));

		var body = await response.Content.ReadAsStringAsync();
		response.StatusCode.Should().Be(HttpStatusCode.OK, body);

		using var json = JsonDocument.Parse(body);
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
			"Bearer", json.RootElement.GetProperty("access_token").GetString());

		return client;
	}

	private async Task<JsonElement> DetailAsync(Guid userId)
	{
		using var admin = await AdminAsync();

		return await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/users/{userId}", Json);
	}

	private async Task<string?> SecurityStampAsync(Guid userId)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return (await context.Users.FindAsync(userId))!.SecurityStamp;
	}

	[Fact]
	public async Task RedefinicaoPeloAdmin_EnviaLinkERevogaNaHora()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		var driver = new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password);
		var (_, refreshToken) = await driver.LoginAsync(email, "openid offline_access logstream");

		using var admin = await AdminAsync();
		var response = await admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/password-reset", null);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		factory.Emails.For(email).Should().ContainSingle()
			.Which.Body.Should().Contain("/conta/redefinir-senha?");

		// Revogação na hora: o pedido costuma nascer de suspeita de comprometimento, e esperar o
		// clique no link deixaria o invasor dentro nesse intervalo.
		(await driver.RefreshAsync(refreshToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task RedefinicaoPeloAdmin_ContaSoCorporativa_Responde409()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var admin = await AdminAsync();

		await admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/local-login", new { enabled = false });
		factory.Emails.For(email).Should().BeEmpty();

		var response = await admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/password-reset", null);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task RedefinicaoPeloAdmin_DeOutroTenant_Responde404()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var outroTenant = await IdentitySeed.TenantAsync(factory);
		using var admin = await AdminAsync();

		var response = await admin.PostAsync($"/api/v1/tenants/{outroTenant}/users/{userId}/password-reset", null);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task DesligarLoginLocal_ApagaASenhaERevoga()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		var driver = new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password);
		var (_, refreshToken) = await driver.LoginAsync(email, "openid offline_access logstream");

		using var admin = await AdminAsync();
		var response = await admin.PostAsJsonAsync(
			$"/api/v1/tenants/{_tenantId}/users/{userId}/local-login", new { enabled = false });

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var detail = await DetailAsync(userId);
		detail.GetProperty("hasPassword").GetBoolean().Should().BeFalse("a credencial antiga não pode sobreviver");
		detail.GetProperty("localLoginEnabled").GetBoolean().Should().BeFalse();
		(await driver.RefreshAsync(refreshToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task DesligarLoginLocal_Repetido_NaoTemEfeitoColateralDeNovo()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var admin = await AdminAsync();

		await admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/local-login", new { enabled = false });
		var stampDepoisDaPrimeira = await SecurityStampAsync(userId);

		var repetida = await admin.PostAsJsonAsync(
			$"/api/v1/tenants/{_tenantId}/users/{userId}/local-login", new { enabled = false });

		// ADR-0034: efeito colateral só na transição real.
		repetida.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await SecurityStampAsync(userId)).Should().Be(stampDepoisDaPrimeira);
		factory.Emails.For(email).Should().BeEmpty();
	}

	[Fact]
	public async Task LigarLoginLocal_EnviaConviteEDeixaAContaSemSenha()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var admin = await AdminAsync();

		await admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/local-login", new { enabled = false });
		var resposta = await admin.PostAsJsonAsync(
			$"/api/v1/tenants/{_tenantId}/users/{userId}/local-login", new { enabled = true });

		resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var detail = await DetailAsync(userId);
		detail.GetProperty("localLoginEnabled").GetBoolean().Should().BeTrue();
		detail.GetProperty("hasPassword").GetBoolean().Should().BeFalse("religar não devolve a senha antiga");
		factory.Emails.For(email).Should().ContainSingle()
			.Which.Body.Should().Contain("/conta/definir-senha?");
	}

	[Fact]
	public async Task LocalLogin_DeOutroTenant_Responde404()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var outroTenant = await IdentitySeed.TenantAsync(factory);
		using var admin = await AdminAsync();

		var response = await admin.PostAsJsonAsync(
			$"/api/v1/tenants/{outroTenant}/users/{userId}/local-login", new { enabled = false });

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task DesligarLoginLocal_ContaNaoEntraMaisPorSenha()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var admin = await AdminAsync();

		await admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/local-login", new { enabled = false });

		using var browser = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			HandleCookies = true,
		});
		var loginPage = await browser.GetAsync("/login");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

		var login = await browser.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.Email"] = email,
			["Input.Password"] = IdentitySeed.Password,
		}));

		// Sem hash de senha, o formulário simplesmente não autentica — com a mesma mensagem
		// genérica de sempre, que não distingue "senha errada" de "conta sem senha local".
		login.StatusCode.Should().Be(HttpStatusCode.OK);
		(await login.Content.ReadAsStringAsync()).Should().Contain("inv");
	}
}
