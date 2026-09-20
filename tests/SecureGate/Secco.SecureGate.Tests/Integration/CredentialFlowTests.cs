using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Convite (ADR-0033): a conta nasce SEM senha e a pessoa a define pelo link — em nenhum momento
/// o admin conhece a credencial. O uso único sai de graça do <c>SecurityStamp</c> embutido no
/// token: definir a senha o troca, e todos os links pendentes daquela conta morrem juntos.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public partial class CredentialFlowTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string ClientId = "credenciais-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string NewPassword = "Nova@Senha123";
	private const string AdminClientId = "credenciais-admin";
	private const string AdminSecret = "credenciais-admin-secret-32-chars-min!";

	private Guid _tenantId;

	[GeneratedRegex(@"https://\S+/conta/definir-senha\?\S+")]
	private static partial Regex InviteLink();

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");

		// Nesta collection o SecureGate valida os PRÓPRIOS tokens: o token forjado HS256 do
		// IdentitySeed não passa, então a gestão fala por client credentials de verdade.
		await factory.CreateClientAsync(AdminClientId, AdminSecret, "securegate:admin");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	/// <summary>Client da gestão com token EMITIDO pelo servidor (scope securegate:admin).</summary>
	private async Task<HttpClient> AdminClientAsync()
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

		using var json = System.Text.Json.JsonDocument.Parse(body);
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
			"Bearer", json.RootElement.GetProperty("access_token").GetString());

		return client;
	}

	private static string Email() => $"convidado-{Guid.NewGuid():N}@secco.test";

	private async Task<(Guid UserId, string Email)> CreateUserAsync(bool localLogin = true)
	{
		var email = Email();
		var response = await (await AdminClientAsync()).PostAsJsonAsync(
			$"/api/v1/tenants/{_tenantId}/users",
			new { email, localLogin, roles = Array.Empty<string>() });

		response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
		var created = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

		return (Guid.Parse(created!["id"].ToString()!), email);
	}

	private string LinkFor(string email)
	{
		var mail = factory.Emails.For(email).Should().NotBeEmpty().And.Subject.Last();
		mail.Body.Should().NotContain(NewPassword, "nenhuma senha viaja por e-mail (ADR-0033)");

		return InviteLink().Match(mail.Body).Value;
	}

	/// <summary>Submete o formulário da página do link, com antiforgery, como um navegador faria.</summary>
	private async Task<HttpResponseMessage> SubmitSetPasswordAsync(string link, string password)
	{
		using var browser = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			HandleCookies = true,
		});

		var path = new Uri(link).PathAndQuery;
		var form = await browser.GetAsync(path);
		var html = await form.Content.ReadAsStringAsync();

		return await browser.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = OidcLoginDriver.ExtractAntiforgeryToken(html),
			["Input.Password"] = password,
			["Input.ConfirmPassword"] = password,
		}));
	}

	[Fact]
	public async Task Convite_CriarUsuario_EnviaLinkEPermiteDefinirSenhaELogar()
	{
		var (_, email) = await CreateUserAsync();

		var response = await SubmitSetPasswordAsync(LinkFor(email), NewPassword);

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);

		var tokens = await new OidcLoginDriver(factory, ClientId, RedirectUri, NewPassword)
			.LoginAsync(email, "openid offline_access logstream");
		tokens.AccessToken.Should().NotBeNullOrEmpty();
	}

	[Fact]
	public async Task Convite_LinkUsadoDuasVezes_SegundaVezRecusada()
	{
		var (_, email) = await CreateUserAsync();
		var link = LinkFor(email);

		(await SubmitSetPasswordAsync(link, NewPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);
		var again = await SubmitSetPasswordAsync(link, "Outra@Senha123");

		again.StatusCode.Should().Be(HttpStatusCode.OK);
		(await again.Content.ReadAsStringAsync()).Should().Contain("pedir outro");
	}

	[Fact]
	public async Task Convite_LinkDeOutraConta_Recusado()
	{
		var (_, primeiro) = await CreateUserAsync();
		var (_, segundo) = await CreateUserAsync();

		var linkDoPrimeiro = new Uri(LinkFor(primeiro));
		var idDoSegundo = new Uri(LinkFor(segundo)).Query.Split("userId=")[1].Split('&')[0];
		var trocado = linkDoPrimeiro.ToString().Replace(
			linkDoPrimeiro.Query.Split("userId=")[1].Split('&')[0], idDoSegundo, StringComparison.Ordinal);

		var response = await SubmitSetPasswordAsync(trocado, NewPassword);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("pedir outro");
	}

	[Fact]
	public async Task CriarUsuario_SemLoginLocal_NaoEnviaConvite()
	{
		var (_, email) = await CreateUserAsync(localLogin: false);

		factory.Emails.For(email).Should().BeEmpty();
	}

	[Fact]
	public async Task CriarUsuario_SemLoginLocal_NascePorDiretorioENaoAceitaSenha()
	{
		var (userId, _) = await CreateUserAsync(localLogin: false);

		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var user = await context.Users.FindAsync(userId);

		user!.LocalLoginEnabled.Should().BeFalse();
		user.PasswordHash.Should().BeNull();
	}

	[Fact]
	public async Task ReenviarConvite_ContaQueJaTemSenha_Responde409()
	{
		var (userId, email) = await CreateUserAsync();
		(await SubmitSetPasswordAsync(LinkFor(email), NewPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);

		var response = await (await AdminClientAsync())
			.PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/invite", null);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task ReenviarConvite_ContaSoCorporativa_Responde409()
	{
		var (userId, _) = await CreateUserAsync(localLogin: false);

		var response = await (await AdminClientAsync())
			.PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/invite", null);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task ReenviarConvite_DeOutroTenant_Responde404()
	{
		var (userId, _) = await CreateUserAsync();
		var outroTenant = await IdentitySeed.TenantAsync(factory);

		var response = await (await AdminClientAsync())
			.PostAsync($"/api/v1/tenants/{outroTenant}/users/{userId}/invite", null);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task ReenviarConvite_Repetido_MandaOutroLinkESoOUltimoContinuaValendo()
	{
		var (userId, email) = await CreateUserAsync();
		var primeiro = LinkFor(email);

		await (await AdminClientAsync()).PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/invite", null);

		// ADR-0034: o propósito do endpoint é o efeito externo, então repetir manda outro e-mail.
		// O que não pode divergir é o ESTADO — e os dois links continuam sendo da mesma conta.
		factory.Emails.For(email).Should().HaveCount(2);
		var segundo = LinkFor(email);
		segundo.Should().NotBe(primeiro);

		(await SubmitSetPasswordAsync(segundo, NewPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);
		(await SubmitSetPasswordAsync(primeiro, "Outra@Senha123")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Convite_SenhaFraca_RecusaEExplicaAPolitica()
	{
		// A política de senha não sumiu com o campo do admin: mudou de lugar, e agora é cobrada
		// de quem de fato escolhe a senha.
		var (_, email) = await CreateUserAsync();

		var response = await SubmitSetPasswordAsync(LinkFor(email), "fraca");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		// Sem acento na asserção: o Razor codifica "política" como entidades HTML.
		(await response.Content.ReadAsStringAsync()).Should().Contain("ao menos 8 caracteres");
	}

	[Fact]
	public async Task CriarUsuario_ComSenhaNoPayload_IgnoraOCampoENascesSemSenha()
	{
		var email = Email();

		var response = await (await AdminClientAsync()).PostAsJsonAsync(
			$"/api/v1/tenants/{_tenantId}/users",
			new { email, password = "Admin@Escolheu1", localLogin = true, roles = Array.Empty<string>() });

		response.StatusCode.Should().Be(HttpStatusCode.Created);

		// O contrato perdeu o campo: um chamador antigo não consegue mais plantar uma senha
		// conhecida pelo admin — e o convite sai do mesmo jeito.
		factory.Emails.For(email).Should().ContainSingle();
	}
}
