using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// O <c>/connect/authorize</c> do login federado direto: sem sessão e com a opção ligada, a pessoa vai
/// direto ao Entra do diretório do tenant — com as guardas contra laço — e nada disso toca a sessão
/// válida, o cookie inválido nem a conta recusada.
/// </summary>
[Collection(EntraFederatedApiCollectionDefinition.Name)]
public class DirectSignInAuthorizeTests(EntraFederatedSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string RedirectUri = "https://app.testes.local/signin-oidc";
	private const string Password = "Direto@Secco1!";
	private const string Scope = "openid profile";
	private const string EntraPrefix = "https://login.microsoftonline.com/";

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private sealed record Scenario(Guid TenantId, string ClientId, Guid DirectoryId, string Email, Guid UserId);

	private async Task<Scenario> ScenarioAsync(bool directSignIn = true, bool withTenant = true)
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		var clientId = $"direct-{Guid.NewGuid():N}";
		var directoryId = Guid.NewGuid();
		var email = $"ana-{Guid.NewGuid():N}@abbc.com.br";

		if (withTenant)
		{
			await factory.CreateTenantPublicClientAsync(tenantId, clientId, RedirectUri);
		}
		else
		{
			await factory.CreatePublicClientAsync(clientId, RedirectUri);
		}

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var federation = new TenantFederation(tenantId, directoryId);
		federation.SetAllowedEmailDomains(["abbc.com.br"]);
		federation.SetDirectSignIn(directSignIn);
		federation.SetEnabled(true);
		context.TenantFederations.Add(federation);
		await context.SaveChangesAsync();

		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
		var user = new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = tenantId,
			UserName = email,
			Email = email,
			EmailConfirmed = true,
		};
		(await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();

		return new Scenario(tenantId, clientId, directoryId, email, user.Id);
	}

	private async Task SetDirectSignInAsync(Guid tenantId, bool enabled)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var federation = await context.TenantFederations.SingleAsync(f => f.TenantId == tenantId);
		federation.SetDirectSignIn(enabled);
		await context.SaveChangesAsync();
	}

	private static string AuthorizeUrl(Scenario s, Dictionary<string, string?>? extra = null)
	{
		var query = new Dictionary<string, string?>
		{
			["response_type"] = "code",
			["client_id"] = s.ClientId,
			["redirect_uri"] = RedirectUri,
			["scope"] = Scope,
			["code_challenge"] = OidcLoginDriver.CreatePkce().Challenge,
			["code_challenge_method"] = "S256",
			["state"] = Guid.NewGuid().ToString("N"),
			["nonce"] = Guid.NewGuid().ToString("N"),
			["login_hint"] = s.Email,
		};

		foreach (var (key, value) in extra ?? [])
		{
			query[key] = value;
		}

		return QueryHelpers.AddQueryString("/connect/authorize", query);
	}

	private OidcLoginDriver Driver(Scenario s) => new(factory, s.ClientId, RedirectUri, Password);

	// Login local pelo driver, com a opção desligada (senão o authorize já iria ao Entra); religa depois
	private async Task<HttpClient> LocalSessionAsync(Scenario s)
	{
		await SetDirectSignInAsync(s.TenantId, false);

		var driver = Driver(s);
		var browser = driver.CreateBrowser();
		var post = await driver.SubmitLoginAsync(browser, s.Email, Scope, OidcLoginDriver.CreatePkce().Challenge);
		post.StatusCode.Should().Be(HttpStatusCode.Redirect);

		await SetDirectSignInAsync(s.TenantId, true);

		return browser;
	}

	// O host de teste devolve Location absoluta (http://localhost/...); o que se compara é o caminho local
	private static string Local(HttpResponseMessage response) =>
		response.Headers.Location!.IsAbsoluteUri ? response.Headers.Location.PathAndQuery : response.Headers.Location.ToString();

	private static void ShouldGoToEntra(HttpResponseMessage response, Scenario s)
	{
		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		response.Headers.Location!.ToString().Should().StartWith($"{EntraPrefix}{s.DirectoryId:D}/oauth2/v2.0/authorize");
	}

	[Fact]
	public async Task Authorize_SemSessao_OpcaoLigada_VaiAoEntraDoDiretorio()
	{
		var s = await ScenarioAsync();
		using var browser = Driver(s).CreateBrowser();

		var response = await browser.GetAsync(AuthorizeUrl(s));

		ShouldGoToEntra(response, s);
		var location = response.Headers.Location!.ToString();
		location.Should().Contain("domain_hint=abbc.com.br");
		location.Should().Contain("login_hint=" + Uri.EscapeDataString(s.Email));
		QueryHelpers.ParseQuery(response.Headers.Location.Query).Should().NotContainKey("prompt");
	}

	[Fact]
	public async Task Authorize_SemSessao_OpcaoDesligada_VaiParaLogin()
	{
		var s = await ScenarioAsync(directSignIn: false);
		using var browser = Driver(s).CreateBrowser();

		var response = await browser.GetAsync(AuthorizeUrl(s));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		Local(response).Should().StartWith("/login");
	}

	[Fact]
	public async Task Authorize_SemSessao_ClientSemTenant_VaiParaLogin()
	{
		var s = await ScenarioAsync(withTenant: false);
		using var browser = Driver(s).CreateBrowser();

		var response = await browser.GetAsync(AuthorizeUrl(s));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		Local(response).Should().StartWith("/login");
	}

	[Fact]
	public async Task Authorize_LoginHintHostil_NaoChegaAoEntra()
	{
		var s = await ScenarioAsync();
		using var browser = Driver(s).CreateBrowser();

		var response = await browser.GetAsync(AuthorizeUrl(s, new() { ["login_hint"] = "ana@abbc.com.br\r\nX-Evil: 1" }));

		ShouldGoToEntra(response, s);
		response.Headers.Location!.ToString().Should().NotContain("login_hint");
	}

	[Fact]
	public async Task Authorize_SemSessaoComMarcaDeTentativa_VaiParaLoginComErro()
	{
		var s = await ScenarioAsync();
		using var browser = Driver(s).CreateBrowser();

		var response = await browser.GetAsync(AuthorizeUrl(s, new() { ["secco_direct_attempt"] = "1" }));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		Local(response).Should().StartWith("/login?federatedError=1&returnUrl=");
	}

	[Fact]
	public async Task Authorize_DuasRequisicoesSemSessao_AmbasVaoAoEntra()
	{
		var s = await ScenarioAsync();
		using var browser = Driver(s).CreateBrowser();

		ShouldGoToEntra(await browser.GetAsync(AuthorizeUrl(s)), s);
		ShouldGoToEntra(await browser.GetAsync(AuthorizeUrl(s)), s);
	}

	[Fact]
	public async Task Authorize_ComMarcaDeLogout_VaiParaSaiu()
	{
		var s = await ScenarioAsync();
		using var browser = Driver(s).CreateBrowser();

		using var request = new HttpRequestMessage(HttpMethod.Get, AuthorizeUrl(s));
		request.Headers.Add("Cookie", $"secco.securegate.signed-out={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
		var response = await browser.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		Local(response).Should().StartWith("/saiu?returnUrl=");
	}

	[Fact]
	public async Task Authorize_PromptNoneSemSessao_LoginRequired()
	{
		var s = await ScenarioAsync();
		using var browser = Driver(s).CreateBrowser();

		var response = await browser.GetAsync(AuthorizeUrl(s, new() { ["prompt"] = "none" }));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		var location = response.Headers.Location!;
		location.ToString().Should().StartWith(RedirectUri);
		QueryHelpers.ParseQuery(location.Query)["error"].ToString().Should().Be("login_required");
	}

	[Fact]
	public async Task Authorize_SessaoComStampInvalido_VaiParaLoginNuncaEntra()
	{
		var s = await ScenarioAsync();
		using var browser = await LocalSessionAsync(s);

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
			var user = await userManager.FindByIdAsync(s.UserId.ToString());
			(await userManager.UpdateSecurityStampAsync(user!)).Succeeded.Should().BeTrue();
		}

		var response = await browser.GetAsync(AuthorizeUrl(s));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		Local(response).Should().StartWith("/login");
	}

	[Fact]
	public async Task Authorize_SessaoDeContaBloqueada_VaiParaLoginNuncaEntra()
	{
		var s = await ScenarioAsync();
		using var browser = await LocalSessionAsync(s);

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
			var user = await userManager.FindByIdAsync(s.UserId.ToString());
			(await userManager.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddHours(1))).Succeeded.Should().BeTrue();
		}

		var response = await browser.GetAsync(AuthorizeUrl(s));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		Local(response).Should().StartWith("/login");
	}

	[Fact]
	public async Task Authorize_PromptLoginComSessao_VaiAoEntraComPromptLogin()
	{
		var s = await ScenarioAsync();
		using var browser = await LocalSessionAsync(s);

		var response = await browser.GetAsync(AuthorizeUrl(s, new() { ["prompt"] = "login" }));

		ShouldGoToEntra(response, s);
		QueryHelpers.ParseQuery(response.Headers.Location!.Query)["prompt"].ToString().Should().Be("login");
	}

	[Fact]
	public async Task Authorize_SessaoValida_EmiteCodeSemPassarPeloEntra()
	{
		var s = await ScenarioAsync();
		using var browser = await LocalSessionAsync(s);

		var response = await browser.GetAsync(AuthorizeUrl(s));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		response.Headers.Location!.ToString().Should().StartWith(RedirectUri);
		QueryHelpers.ParseQuery(response.Headers.Location.Query).Should().ContainKey("code");
	}

	[Fact]
	public async Task Callback_SemCookieExterno_VoltaParaLoginComReturnUrl()
	{
		var s = await ScenarioAsync();
		using var browser = Driver(s).CreateBrowser();

		var local = await browser.GetAsync("/login/entra-callback?returnUrl=%2Fconnect%2Fauthorize%3Fx%3D1");
		var hostil = await browser.GetAsync("/login/entra-callback?returnUrl=https%3A%2F%2Fevil.example");

		Local(local).Should().Be("/login?federatedError=1&returnUrl=%2Fconnect%2Fauthorize%3Fx%3D1");
		Local(hostil).Should().Be("/login?federatedError=1");
	}
}
