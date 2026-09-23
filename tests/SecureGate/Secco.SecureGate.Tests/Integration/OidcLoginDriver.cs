using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Dirige o fluxo real do navegador contra o SecureGate — authorize → tela de login (antiforgery)
/// → code → troca com PKCE → refresh — para os testes obterem tokens EMITIDOS de verdade.
/// </summary>
internal sealed partial class OidcLoginDriver(
	SecureGateApiFactory factory,
	string clientId,
	string redirectUri,
	string password)
{
	[GeneratedRegex("__RequestVerificationToken.*?value=\"([^\"]+)\"", RegexOptions.Singleline)]
	private static partial Regex AntiforgeryField();

	/// <summary>Token antiforgery de uma página renderizada — as telas de credencial também o exigem.</summary>
	/// <param name="html">HTML da página.</param>
	public static string ExtractAntiforgeryToken(string html) => AntiforgeryField().Match(html).Groups[1].Value;

	public HttpClient CreateBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions
	{
		AllowAutoRedirect = false,
		HandleCookies = true,
	});

	public HttpClient BearerClient(string accessToken)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		return client;
	}

	public async Task<(string AccessToken, string RefreshToken)> LoginAsync(string email, string scope)
	{
		using var browser = CreateBrowser();
		var (verifier, code) = await ObtainCodeAsync(browser, email, scope);

		var response = await ExchangeCodeAsync(browser, code, verifier);
		var body = await response.Content.ReadAsStringAsync();
		response.StatusCode.Should().Be(HttpStatusCode.OK, body);

		using var json = JsonDocument.Parse(body);

		return (json.RootElement.GetProperty("access_token").GetString()!, json.RootElement.GetProperty("refresh_token").GetString()!);
	}

	public async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
	{
		using var client = factory.CreateClient();

		return await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["refresh_token"] = refreshToken,
			["client_id"] = clientId,
		}));
	}

	public async Task<(string Verifier, string Code)> ObtainCodeAsync(HttpClient browser, string email, string scope)
	{
		var (verifier, challenge) = CreatePkce();

		var loginPost = await SubmitLoginAsync(browser, email, scope, challenge);
		loginPost.StatusCode.Should().Be(HttpStatusCode.Redirect, "credenciais válidas voltam ao authorize");

		var codeResponse = await browser.GetAsync(loginPost.Headers.Location!.ToString());
		codeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

		return (verifier, QueryHelpers.ParseQuery(codeResponse.Headers.Location!.Query)["code"].ToString());
	}

	public async Task<HttpResponseMessage> SubmitLoginAsync(HttpClient browser, string email, string scope, string challenge)
	{
		var loginUrl = (await browser.GetAsync(AuthorizeUrl(scope, challenge))).Headers.Location!.ToString();
		var loginPage = await browser.GetAsync(loginUrl);
		loginPage.EnsureSuccessStatusCode();
		var antiforgery = AntiforgeryField().Match(await loginPage.Content.ReadAsStringAsync()).Groups[1].Value;

		var senha = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["Input.Email"] = email,
			["Input.Password"] = password,
			["__RequestVerificationToken"] = antiforgery,
		}));

		return await CompleteTwoFactorIfNeededAsync(browser, senha, email);
	}

	/// <summary>
	/// Faz o segundo passo quando a conta tem 2FA (entrega D), como um navegador real faria: o
	/// código sai do cálculo TOTP sobre a chave cadastrada, que é o que o aplicativo da pessoa
	/// mostraria. Contas sem segundo fator passam direto.
	/// </summary>
	public async Task<HttpResponseMessage> CompleteTwoFactorIfNeededAsync(
		HttpClient browser,
		HttpResponseMessage passwordResponse,
		string email)
	{
		if (passwordResponse.Headers.Location is not { } destino
			|| !destino.ToString().StartsWith("/login/dois-fatores", StringComparison.OrdinalIgnoreCase))
		{
			return passwordResponse;
		}

		using var scope = factory.Services.CreateScope();
		var userManager = scope.ServiceProvider
			.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Secco.SecureGate.Infrastructure.Identity.User>>();
		var user = await userManager.FindByEmailAsync(email);
		var key = await userManager.GetAuthenticatorKeyAsync(user!);

		// O destino CARREGA o returnUrl: sem ele, o segundo passo terminaria em /conta e a
		// requisição de autorização original se perderia.
		var segundoPasso = destino.ToString();
		var page = await browser.GetAsync(segundoPasso);
		var antiforgery = AntiforgeryField().Match(await page.Content.ReadAsStringAsync()).Groups[1].Value;

		return await browser.PostAsync(segundoPasso, new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = antiforgery,
			["Input.Code"] = TotpCalculator.Compute(key!),
		}));
	}

	/// <summary>Chama o authorize com o cookie que o navegador já tem, sem passar pelo formulário.</summary>
	public Task<HttpResponseMessage> AuthorizeAsync(HttpClient browser, string scope) =>
		browser.GetAsync(AuthorizeUrl(scope, CreatePkce().Challenge));

	private string AuthorizeUrl(string scope, string challenge) =>
		QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
		{
			["response_type"] = "code",
			["client_id"] = clientId,
			["redirect_uri"] = redirectUri,
			["scope"] = scope,
			["code_challenge"] = challenge,
			["code_challenge_method"] = "S256",
			["state"] = Guid.NewGuid().ToString("N"),
			["nonce"] = Guid.NewGuid().ToString("N"),
		});

	public Task<HttpResponseMessage> ExchangeCodeAsync(HttpClient browser, string code, string verifier) =>
		browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code",
			["code"] = code,
			["redirect_uri"] = redirectUri,
			["client_id"] = clientId,
			["code_verifier"] = verifier,
		}));

	public static (string Verifier, string Challenge) CreatePkce()
	{
		var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));

		return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
	}

	/// <summary>Scopes do access token — o OpenIddict os emite numa claim separada por espaços.</summary>
	public static IReadOnlyList<string> Scopes(string accessToken) =>
	[
		.. new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Claims
			.Where(claim => claim.Type == "scope")
			.SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
	];

	private static string Base64Url(byte[] bytes) =>
		Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
