using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Login em dois passos (entrega D): senha, depois dígito do autenticador ou código de recuperação.
/// </summary>
/// <remarks>
/// O que separa os dois passos é o cookie de duas etapas do Identity, que não autentica nada
/// sozinho. Sem ele — ou seja, sem ter passado pela senha — não existe segundo passo.
/// </remarks>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class TwoFactorLoginTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"login-2fa-{Guid.NewGuid():N}@secco.test";

	private HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions
	{
		AllowAutoRedirect = false,
		HandleCookies = true,
	});

	/// <summary>Cria a conta já com o segundo fator ligado e devolve a chave do autenticador.</summary>
	private async Task<(string Email, string Key, IReadOnlyList<string> RecoveryCodes)> UserWith2FaAsync()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
		var enable = scope.ServiceProvider.GetRequiredService<EnableTwoFactorHandler>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		await setup.StartEnrollmentAsync(userId);
		var user = await userManager.FindByIdAsync(userId.ToString());
		var key = (await userManager.GetAuthenticatorKeyAsync(user!))!;
		var codes = await enable.HandleAsync(userId, TotpCalculator.Compute(key));

		codes.Should().HaveCount(10, "o cadastro precisa ter concluído para o login pedir o segundo fator");

		return (email, key, codes);
	}

	private async Task<HttpResponseMessage> SubmitLoginAsync(HttpClient browser, string email, string password)
	{
		var page = await browser.GetAsync("/login");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

		return await browser.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.Email"] = email,
			["Input.Password"] = password,
		}));
	}

	private static async Task<HttpResponseMessage> SubmitCodeAsync(HttpClient browser, string code, bool recovery = false)
	{
		var page = await browser.GetAsync("/login/dois-fatores");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

		var form = new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.Code"] = code,
		};

		if (recovery)
		{
			form["Input.IsRecoveryCode"] = "true";
		}

		return await browser.PostAsync("/login/dois-fatores", new FormUrlEncodedContent(form));
	}

	[Fact]
	public async Task Login_ComSenhaCerta_PedeOSegundoFator()
	{
		var (email, _, _) = await UserWith2FaAsync();
		using var browser = Browser();

		var senha = await SubmitLoginAsync(browser, email, IdentitySeed.Password);

		senha.StatusCode.Should().Be(HttpStatusCode.Redirect);
		senha.Headers.Location!.ToString().Should().StartWith("/login/dois-fatores");

		// A senha sozinha não abriu sessão nenhuma.
		(await browser.GetAsync("/conta")).StatusCode.Should().Be(HttpStatusCode.Redirect);
	}

	[Fact]
	public async Task Login_ComDigitoValido_Entra()
	{
		var (email, key, _) = await UserWith2FaAsync();
		using var browser = Browser();
		await SubmitLoginAsync(browser, email, IdentitySeed.Password);

		var resposta = await SubmitCodeAsync(browser, TotpCalculator.Compute(key));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		(await browser.GetAsync("/conta")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Login_ComDigitoErrado_NaoEntra()
	{
		var (email, _, _) = await UserWith2FaAsync();
		using var browser = Browser();
		await SubmitLoginAsync(browser, email, IdentitySeed.Password);

		var resposta = await SubmitCodeAsync(browser, "000000");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		(await resposta.Content.ReadAsStringAsync()).Should().Contain("inv");
		(await browser.GetAsync("/conta")).StatusCode.Should().Be(HttpStatusCode.Redirect);
	}

	[Fact]
	public async Task Login_ComCodigoDeRecuperacao_EntraEOCodigoNaoServeDeNovo()
	{
		var (email, _, codigos) = await UserWith2FaAsync();

		using var primeira = Browser();
		await SubmitLoginAsync(primeira, email, IdentitySeed.Password);
		(await SubmitCodeAsync(primeira, codigos[0], recovery: true)).StatusCode.Should().Be(HttpStatusCode.Redirect);

		using var segunda = Browser();
		await SubmitLoginAsync(segunda, email, IdentitySeed.Password);

		// Cada código vale uma vez: reusar é o mesmo que não ter código.
		var reuso = await SubmitCodeAsync(segunda, codigos[0], recovery: true);

		reuso.StatusCode.Should().Be(HttpStatusCode.OK);
		(await segunda.GetAsync("/conta")).StatusCode.Should().Be(HttpStatusCode.Redirect);
	}

	[Fact]
	public async Task SegundoPasso_SemTerPassadoPelaSenha_NaoAbre()
	{
		using var browser = Browser();

		var resposta = await browser.GetAsync("/login/dois-fatores");

		// Sem o cookie de duas etapas não há segundo passo — ele é o que carrega o primeiro.
		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		// A rota do Razor não distingue caixa; o que importa é o destino.
		resposta.Headers.Location!.ToString().Should().ContainEquivalentOf("/login");
	}

	[Fact]
	public async Task Login_ContaSem2Fa_EntraDiretoComoAntes()
	{
		var email = Email();
		await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var browser = Browser();

		var senha = await SubmitLoginAsync(browser, email, IdentitySeed.Password);

		senha.Headers.Location!.ToString().Should().Be("/conta");
		(await browser.GetAsync("/conta")).StatusCode.Should().Be(HttpStatusCode.OK);
	}
}
