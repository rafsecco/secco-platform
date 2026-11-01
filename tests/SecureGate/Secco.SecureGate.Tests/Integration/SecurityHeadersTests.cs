using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Cabeçalhos de segurança nas telas do SecureGate (ADR-0057): login nunca emoldurado, CSP com
/// nonce no único estilo inline, nenhum atributo <c>style=</c>, e o fluxo OIDC intacto sob a política.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public partial class SecurityHeadersTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "webapp-csp";
	private const string RedirectUri = "https://localhost/callback-csp";
	private const string Password = "L0gin@Secco!";

	private readonly string _email = $"csp-{Guid.NewGuid():N}@secco.test";

	[GeneratedRegex("'nonce-([^']+)'")]
	private static partial Regex CspNonce();

	[GeneratedRegex("<style nonce=\"([^\"]+)\">")]
	private static partial Regex StyleNonce();

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");

		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		var tenant = new Tenant("Tenant da CSP", $"t-{Guid.NewGuid():N}");
		context.Tenants.Add(tenant);
		await context.SaveChangesAsync();

		var user = new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = tenant.Id,
			UserName = _email,
			Email = _email,
			EmailConfirmed = true,
		};
		(await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CreateBrowser() => secureGate.CreateClient(new WebApplicationFactoryClientOptions
	{
		AllowAutoRedirect = false,
		HandleCookies = true,
	});

	private static string Header(HttpResponseMessage response, string name)
		=> response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : string.Empty;

	private static string AuthorizeUrl(string? responseMode, string? state = null) => QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
	{
		["response_type"] = "code",
		["client_id"] = ClientId,
		["redirect_uri"] = RedirectUri,
		["scope"] = "openid profile",
		["code_challenge"] = OidcLoginDriver.CreatePkce().Challenge,
		["code_challenge_method"] = "S256",
		["state"] = state ?? Guid.NewGuid().ToString("N"),
		["nonce"] = Guid.NewGuid().ToString("N"),
		["response_mode"] = responseMode,
	});

	private async Task<HttpResponseMessage> LoginAndAuthorizeAsync(HttpClient browser, string authorizeUrl)
	{
		var loginUrl = (await browser.GetAsync(authorizeUrl)).Headers.Location!.ToString();
		var loginPage = await browser.GetAsync(loginUrl);
		var loginPost = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["Input.Email"] = _email,
			["Input.Password"] = Password,
			["__RequestVerificationToken"] = OidcLoginDriver.ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync()),
		}));
		loginPost.StatusCode.Should().Be(HttpStatusCode.Redirect);

		return await browser.GetAsync(loginPost.Headers.Location!.ToString());
	}

	[Fact]
	public async Task Tela_de_login_nunca_e_emoldurada_e_o_estilo_inline_leva_o_nonce_do_cabecalho()
	{
		using var browser = CreateBrowser();

		var response = await browser.GetAsync("/login");
		var html = await response.Content.ReadAsStringAsync();

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		Header(response, "X-Frame-Options").Should().Be("DENY", "o antiforgery poria SAMEORIGIN; o SDK sobrescreve");
		Header(response, "X-Content-Type-Options").Should().Be("nosniff");

		var csp = Header(response, "Content-Security-Policy");
		csp.Should().Contain("frame-ancestors 'none'").And.Contain("style-src-attr 'none'").And.NotContain("form-action");
		StyleNonce().Match(html).Groups[1].Value.Should().Be(CspNonce().Match(csp).Groups[1].Value);
		html.Should().NotContain("style=\"", "atributo de estilo não aceita nonce e seria bloqueado");
		html.Should().NotContain("secco-nonce");
		response.Headers.CacheControl!.NoStore.Should().BeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("form_post")]
	public async Task Fluxo_de_login_conclui_sob_a_politica(string? responseMode)
	{
		using var browser = CreateBrowser();

		var codeResponse = await LoginAndAuthorizeAsync(browser, AuthorizeUrl(responseMode));

		if (responseMode is null)
		{
			codeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
			codeResponse.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);
			return;
		}

		// form_post (o padrão do handler OIDC da Microsoft): a página que entrega o code ao client
		// precisa funcionar sob a CSP — nenhum script inline sem o nonce do cabeçalho.
		var html = await codeResponse.Content.ReadAsStringAsync();
		codeResponse.StatusCode.Should().Be(HttpStatusCode.OK, html);
		html.Should().Contain($"action=\"{RedirectUri}\"");

		html.Should().MatchRegex("<input type=\"hidden\" name=\"code\" value=\"[^\"]+\" />");

		var nonce = CspNonce().Match(Header(codeResponse, "Content-Security-Policy")).Groups[1].Value;
		var scripts = Regex.Matches(html, "<script[^>]*>");
		scripts.Should().NotBeEmpty();
		foreach (Match script in scripts)
		{
			script.Value.Should().Contain($"nonce=\"{nonce}\"", $"script inline sem nonce é bloqueado pela CSP. HTML: {html}");
		}

		codeResponse.Headers.CacheControl!.NoStore.Should().BeTrue("a página carrega o code e o nonce");
	}

	[Fact]
	public async Task Form_post_codifica_o_state_hostil_vindo_do_client()
	{
		// O state é opaco para o servidor e volta na página, servida na origem do SecureGate.
		const string hostile = "x\"><img src=x onerror=alert(1)><script>alert(2)</script>";
		using var browser = CreateBrowser();

		var codeResponse = await LoginAndAuthorizeAsync(browser, AuthorizeUrl("form_post", hostile));
		var html = await codeResponse.Content.ReadAsStringAsync();

		codeResponse.StatusCode.Should().Be(HttpStatusCode.OK, html);
		html.Should().NotContain("<img").And.NotContain("alert(2)</script>");
		html.Should().Contain("name=\"state\" value=\"x&quot;&gt;&lt;img");
	}
}
