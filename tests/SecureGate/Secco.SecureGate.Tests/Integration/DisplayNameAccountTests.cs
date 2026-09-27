using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using FluentAssertions;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Nome de exibição pelo próprio dono, na tela da conta (#30), e o efeito na claim <c>name</c> do
/// token — o ponto que fecha o pedido da issue: o produto exibe o nome sem chamar a API de novo.
/// </summary>
/// <remarks>
/// A claim <c>name</c> hoje carrega o e-mail (ADR-0007/ADR-0022): trocar o VALOR por um fallback
/// (nome de exibição, senão o e-mail) é aditivo — quem já lê "name" hoje (o rótulo de ator na
/// auditoria do secco-intranet, por exemplo) continua recebendo algo, nunca um claim ausente.
/// </remarks>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class DisplayNameAccountTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string ClientId = "nome-exibicao-e2e";
	private const string RedirectUri = "https://localhost/callback";

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"nome-{Guid.NewGuid():N}@secco.test";

	private OidcLoginDriver Driver => new(factory, ClientId, RedirectUri, IdentitySeed.Password);

	private async Task<HttpClient> SignedInBrowserAsync(string email)
	{
		var browser = Driver.CreateBrowser();
		var page = await browser.GetAsync("/login");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

		var login = await browser.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.Email"] = email,
			["Input.Password"] = IdentitySeed.Password,
		}));

		login.StatusCode.Should().Be(HttpStatusCode.Redirect);

		return browser;
	}

	private async Task SetDisplayNameDirectAsync(Guid userId, string displayName)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var user = await context.Users.FirstAsync(u => u.Id == userId);
		user.DisplayName = displayName;
		await context.SaveChangesAsync();
	}

	[Fact]
	public async Task Dono_DefineOProprioNome_PelaConta()
	{
		var email = Email();
		await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var browser = await SignedInBrowserAsync(email);

		var pagina = await browser.GetAsync("/conta/nome");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await pagina.Content.ReadAsStringAsync());

		var resposta = await browser.PostAsync("/conta/nome", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.DisplayName"] = "Rafael Secco",
		}));

		resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
		resposta.Headers.Location!.ToString().Should().StartWith("/conta");

		var conta = await browser.GetAsync(resposta.Headers.Location!.ToString());
		(await conta.Content.ReadAsStringAsync()).Should().Contain("Rafael Secco");
	}

	[Fact]
	public async Task Dono_NomeComCaractereDeControle_MostraErroENaoGrava()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		using var browser = await SignedInBrowserAsync(email);

		var pagina = await browser.GetAsync("/conta/nome");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await pagina.Content.ReadAsStringAsync());

		// Vertical tab (U+000B): caractere de controle que sobrevive ao encode do form sem disparar
		// uma rejeição da própria pilha HTTP antes de chegar à regra (o NUL faz isso, e testaria o
		// framework, não a regra).
		var resposta = await browser.PostAsync("/conta/nome", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.DisplayName"] = "Rafael\u000BSecco",
		}));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);

		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.Users.FirstAsync(u => u.Id == userId)).DisplayName.Should().BeNull();
	}

	[Fact]
	public async Task Dono_SemSessao_RedirecionaParaOLogin()
	{
		using var anonimo = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

		var response = await anonimo.GetAsync("/conta/nome");

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		response.Headers.Location!.ToString().Should().Contain("/login");
	}

	[Fact]
	public async Task Claim_Name_CaiNoEmailQuandoSemNomeDeExibicao()
	{
		var email = Email();
		await IdentitySeed.UserAsync(factory, _tenantId, email);

		var (accessToken, _) = await Driver.LoginAsync(email, "openid offline_access logstream");

		new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetClaim("name").Value.Should().Be(email);
	}

	[Fact]
	public async Task Claim_Name_UsaNomeDeExibicaoQuandoDefinido()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		await SetDisplayNameDirectAsync(userId, "Rafael Secco");

		var (accessToken, _) = await Driver.LoginAsync(email, "openid offline_access logstream");

		new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetClaim("name").Value.Should().Be("Rafael Secco");
	}
}
