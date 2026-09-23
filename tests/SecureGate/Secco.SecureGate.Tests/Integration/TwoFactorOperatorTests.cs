using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Segundo fator obrigatório para o operador de instalação (entrega D, ADR-0030).
/// </summary>
/// <remarks>
/// A exigência age no <c>/connect/authorize</c>, <b>antes</b> de o código de autorização sair — é
/// o que permite ao AdminPortal não saber de nada, sendo ele apenas relying party (ADR-0023).
/// </remarks>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class TwoFactorOperatorTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string ClientId = "operador-2fa-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string Scope = "openid offline_access securegate:admin";

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreatePublicClientAsync(ClientId, RedirectUri, "securegate:admin", "logstream");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"op-2fa-{Guid.NewGuid():N}@secco.test";

	private HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions
	{
		AllowAutoRedirect = false,
		HandleCookies = true,
	});

	private OidcLoginDriver Driver => new(factory, ClientId, RedirectUri, IdentitySeed.Password);

	/// <summary>Cria um operador de instalação (papel no tenant de plataforma).</summary>
	private async Task<(Guid UserId, string Email)> OperatorAsync()
	{
		var email = Email();
		var userId = await IdentitySeed.PlatformOperatorAsync(factory, email);

		return (userId, email);
	}

	private async Task EnableTwoFactorAsync(Guid userId)
	{
		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
		var enable = scope.ServiceProvider.GetRequiredService<EnableTwoFactorHandler>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		await setup.StartEnrollmentAsync(userId);
		var user = await userManager.FindByIdAsync(userId.ToString());
		var codes = await enable.HandleAsync(userId, TotpCalculator.Compute((await userManager.GetAuthenticatorKeyAsync(user!))!));

		codes.Should().HaveCount(10);
	}

	/// <summary>
	/// Navegador autenticado, percorrendo os dois passos quando a conta já tem segundo fator —
	/// senão a senha sozinha deixa o navegador sem sessão nenhuma.
	/// </summary>
	private async Task<HttpClient> SignedInBrowserAsync(string email, Guid? userIdWith2Fa = null)
	{
		var browser = Browser();
		var page = await browser.GetAsync("/login");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

		var login = await browser.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.Email"] = email,
			["Input.Password"] = IdentitySeed.Password,
		}));

		login.StatusCode.Should().Be(HttpStatusCode.Redirect);

		if (userIdWith2Fa is not { } userId)
		{
			return browser;
		}

		login.Headers.Location!.ToString().Should().StartWith("/login/dois-fatores");

		var segundoPasso = await browser.GetAsync("/login/dois-fatores");
		var segundoToken = OidcLoginDriver.ExtractAntiforgeryToken(await segundoPasso.Content.ReadAsStringAsync());
		var codigo = TotpCalculator.Compute(await CurrentKeyAsync(userId));

		var conclusao = await browser.PostAsync("/login/dois-fatores", new FormUrlEncodedContent(
			new Dictionary<string, string>
			{
				["__RequestVerificationToken"] = segundoToken,
				["Input.Code"] = codigo,
			}));

		conclusao.StatusCode.Should().Be(HttpStatusCode.Redirect);

		return browser;
	}

	private async Task<string> CurrentKeyAsync(Guid userId)
	{
		using var scope = factory.Services.CreateScope();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
		var user = await userManager.FindByIdAsync(userId.ToString());

		return (await userManager.GetAuthenticatorKeyAsync(user!))!;
	}

	[Fact]
	public async Task Operador_Sem2Fa_NaoObtemCodigoDeAutorizacao()
	{
		var (_, email) = await OperatorAsync();
		using var browser = await SignedInBrowserAsync(email);

		var authorize = await Driver.AuthorizeAsync(browser, Scope);

		authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
		authorize.Headers.Location!.ToString().Should().Contain("/conta/dois-fatores");
		authorize.Headers.Location!.ToString().Should().NotStartWith(RedirectUri, "nenhum código pode sair antes do cadastro");
	}

	[Fact]
	public async Task Operador_ComCadastroFeito_ObtemCodigo()
	{
		var (userId, email) = await OperatorAsync();
		await EnableTwoFactorAsync(userId);
		using var browser = await SignedInBrowserAsync(email, userId);

		var authorize = await Driver.AuthorizeAsync(browser, Scope);

		authorize.Headers.Location!.ToString().Should().StartWith(RedirectUri);
	}

	[Fact]
	public async Task UsuarioComum_Sem2Fa_ObtemCodigoNormalmente()
	{
		var email = Email();
		await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var browser = await SignedInBrowserAsync(email);

		// A exigência é só do operador: 2FA segue voluntário para todo mundo (entrega D).
		var authorize = await Driver.AuthorizeAsync(browser, "openid offline_access logstream");

		authorize.Headers.Location!.ToString().Should().StartWith(RedirectUri);
	}

	[Fact]
	public async Task Operador_NaoDesligaOProprioSegundoFator()
	{
		var (userId, email) = await OperatorAsync();
		await EnableTwoFactorAsync(userId);
		using var browser = await SignedInBrowserAsync(email, userId);

		var html = await (await browser.GetAsync("/conta/dois-fatores")).Content.ReadAsStringAsync();

		// O botão nem aparece…
		html.Should().NotContain("Desativar segundo fator");

		// …e como a página do operador não tem formulário, o antiforgery vem de outra tela da
		// MESMA sessão — senão o POST seria recusado pelo antiforgery e a guarda ficaria sem prova.
		var token = OidcLoginDriver.ExtractAntiforgeryToken(
			await (await browser.GetAsync("/conta/trocar-senha")).Content.ReadAsStringAsync());

		// …e o POST direto também não passa: esconder controle não é proteger.
		var resposta = await browser.PostAsync("/conta/dois-fatores?handler=Disable", new FormUrlEncodedContent(
			new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("operadores da instala");

		using var scope = factory.Services.CreateScope();
		(await scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>().GetStateAsync(userId))!
			.Enabled.Should().BeTrue();
	}
}
