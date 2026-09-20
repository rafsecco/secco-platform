using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Troca da própria senha (ADR-0033): exige a senha atual, derruba as outras sessões e mantém a
/// de quem trocou.
/// </summary>
/// <remarks>
/// Exigir a senha atual é o que separa "o dono trocou a senha" de "quem roubou o cookie trocou a
/// senha e trancou o dono para fora".
/// </remarks>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class ChangeOwnPasswordTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string ClientId = "troca-senha-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string NewPassword = "Troca@Senha123";
	private const string Page = "/conta/trocar-senha";

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"troca-{Guid.NewGuid():N}@secco.test";

	/// <summary>Navegador autenticado pelo cookie do login interativo.</summary>
	private async Task<HttpClient> SignedInBrowserAsync(string email, string password)
	{
		var browser = factory.CreateClient(new WebApplicationFactoryClientOptions
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
			["Input.Password"] = password,
		}));

		login.StatusCode.Should().Be(HttpStatusCode.Redirect, "o login por senha cria o cookie e redireciona");

		return browser;
	}

	private static async Task<HttpResponseMessage> SubmitAsync(HttpClient browser, string current, string next)
	{
		var form = await browser.GetAsync(Page);
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await form.Content.ReadAsStringAsync());

		return await browser.PostAsync(Page, new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.CurrentPassword"] = current,
			["Input.Password"] = next,
			["Input.ConfirmPassword"] = next,
		}));
	}

	[Fact]
	public void EsquemaDoCookie_ContinuaSendoOEsperadoPeloAtributo() =>
		// O [Authorize] da página usa o literal, porque a constante do Identity não é constante de
		// compilação. Se o framework mudar o nome, é aqui que se descobre — e não em produção,
		// com a página virando anônima ou inacessível.
		IdentityConstants.ApplicationScheme.Should().Be("Identity.Application");

	[Fact]
	public async Task TrocarSenha_SemSessao_RedirecionaParaOLogin()
	{
		using var anonimo = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

		var response = await anonimo.GetAsync(Page);

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		response.Headers.Location!.ToString().Should().Contain("/login");
	}

	[Fact]
	public async Task TrocarSenha_ComSenhaAtualErrada_Recusa()
	{
		var email = Email();
		await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);

		var response = await SubmitAsync(browser, current: "Errada@Senha1", next: NewPassword);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("Senha atual incorreta");

		// A senha não mudou: o login com a antiga continua funcionando.
		using var outro = await SignedInBrowserAsync(email, IdentitySeed.Password);
	}

	[Fact]
	public async Task TrocarSenha_PeloDono_MantemASessaoAtualEDerrubaAsOutras()
	{
		var email = Email();
		await IdentitySeed.UserAsync(factory, _tenantId, email);

		var driver = new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password);
		var (_, refreshDaOutraSessao) = await driver.LoginAsync(email, "openid offline_access logstream");

		using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);
		var response = await SubmitAsync(browser, current: IdentitySeed.Password, next: NewPassword);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("Senha alterada");

		// A sessão desta janela segue viva: o cookie foi renovado contra o novo stamp.
		(await browser.GetAsync(Page)).StatusCode.Should().Be(HttpStatusCode.OK);

		// A outra sessão caiu (ADR-0032).
		(await driver.RefreshAsync(refreshDaOutraSessao)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

		// E a nova senha é a que vale daqui em diante.
		var comNova = new OidcLoginDriver(factory, ClientId, RedirectUri, NewPassword);
		(await comNova.LoginAsync(email, "openid offline_access logstream")).AccessToken.Should().NotBeNullOrEmpty();
	}

	[Fact]
	public async Task TrocarSenha_AvisaODonoSemRevelarASenha()
	{
		var email = Email();
		await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);

		await SubmitAsync(browser, current: IdentitySeed.Password, next: NewPassword);

		var aviso = factory.Emails.For(email).Should().ContainSingle().Subject;
		aviso.Subject.Should().Contain("alterada");
		aviso.Body.Should().NotContain(NewPassword).And.NotContain(IdentitySeed.Password);
	}

	[Fact]
	public async Task TrocarSenha_ContaSoCorporativa_Responde404()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);

		using (var scope = factory.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			var user = await context.Users.FindAsync(userId);
			user!.LocalLoginEnabled = false;
			await context.SaveChangesAsync();
		}

		// 404 e não 403: a resposta é a de rota inexistente, sem contar nada sobre a conta.
		(await browser.GetAsync(Page)).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task TrocarSenha_SenhaFraca_RecusaSemDerrubarSessao()
	{
		var email = Email();
		await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var browser = await SignedInBrowserAsync(email, IdentitySeed.Password);

		var response = await SubmitAsync(browser, current: IdentitySeed.Password, next: "fraca");

		(await response.Content.ReadAsStringAsync()).Should().Contain("ao menos 8 caracteres");
		factory.Emails.For(email).Should().BeEmpty("senha recusada não é evento de senha");
	}
}
