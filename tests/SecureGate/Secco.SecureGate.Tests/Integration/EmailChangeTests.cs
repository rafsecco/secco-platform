using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Troca do próprio e-mail (entrega C).
/// </summary>
/// <remarks>
/// O e-mail é o username da plataforma (ADR-0022) e, no primeiro login federado, a chave que casa
/// a pessoa com o diretório (ADR-0026). Por isso a troca é confirmada no endereço NOVO e move o
/// <c>SecurityStamp</c>, derrubando sessões e links pendentes (ADR-0032/0033).
/// </remarks>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public partial class EmailChangeTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string ClientId = "troca-email-e2e";
	private const string RedirectUri = "https://localhost/callback";

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"troca-email-{Guid.NewGuid():N}@secco.test";

	private async Task<(Guid UserId, string Email)> UserAsync()
	{
		var email = Email();

		return (await IdentitySeed.UserAsync(factory, _tenantId, email), email);
	}

	[Fact]
	public async Task Token_DeTrocaDeEmail_SoValeParaOEnderecoQueOGerou()
	{
		var (userId, _) = await UserAsync();
		var destino = Email();
		var outro = Email();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
		var token = await tokens.CreateEmailChangeTokenAsync(userId, destino);

		// O endereço novo faz parte do propósito do token: um link interceptado não serve para
		// apontar a conta a outro lugar.
		(await tokens.ChangeEmailAsync(userId, outro, token)).Should().Be(CredentialTokenOutcome.InvalidToken);
		(await tokens.ChangeEmailAsync(userId, destino, token)).Should().Be(CredentialTokenOutcome.Done);
	}

	[Fact]
	public async Task TrocaDeEmail_MudaTambemOUserName()
	{
		var (userId, _) = await UserAsync();
		var destino = Email();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
		await tokens.ChangeEmailAsync(userId, destino, await tokens.CreateEmailChangeTokenAsync(userId, destino));

		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var user = await context.Users.FindAsync(userId);

		user!.Email.Should().Be(destino);
		// E-mail e username são a mesma coisa (ADR-0022): deixar um para trás faria a pessoa
		// continuar logando com o endereço antigo depois de trocá-lo.
		user.UserName.Should().Be(destino);
		user.NormalizedUserName.Should().Be(destino.ToUpperInvariant());
	}

	[Fact]
	public async Task TrocaDeEmail_ParaEnderecoEmUso_NaoAcontece()
	{
		var (userId, original) = await UserAsync();
		var (_, ocupado) = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
		var token = await tokens.CreateEmailChangeTokenAsync(userId, ocupado);

		(await tokens.ChangeEmailAsync(userId, ocupado, token)).Should().Be(CredentialTokenOutcome.NotAllowed);

		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.Users.FindAsync(userId))!.Email.Should().Be(original);
	}

	[Fact]
	public async Task EmailJaEmUso_NaoEstaDisponivel()
	{
		var (_, existente) = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();

		(await tokens.IsEmailAvailableAsync(existente)).Should().BeFalse();
		(await tokens.IsEmailAvailableAsync(Email())).Should().BeTrue();
	}

	[Fact]
	public async Task ConferirSenha_SoAceitaASenhaCerta()
	{
		var (userId, _) = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();

		(await tokens.CheckPasswordAsync(userId, IdentitySeed.Password)).Should().BeTrue();
		(await tokens.CheckPasswordAsync(userId, "Errada@Senha1")).Should().BeFalse();
	}

	[Fact]
	public async Task Pedido_ComSenhaErrada_NaoEnviaNada()
	{
		var (userId, atual) = await UserAsync();
		var destino = Email();

		using var scope = factory.Services.CreateScope();
		var handler = scope.ServiceProvider.GetRequiredService<RequestEmailChangeHandler>();

		(await handler.HandleAsync(userId, destino, "Errada@Senha1", remoteAddress: null)).Should().BeFalse();
		factory.Emails.For(destino).Should().BeEmpty();
		factory.Emails.For(atual).Should().BeEmpty();
	}

	[Fact]
	public async Task Pedido_Valido_MandaLinkAoNovoEAvisoAoAntigo()
	{
		var (userId, atual) = await UserAsync();
		var destino = Email();

		using var scope = factory.Services.CreateScope();
		var handler = scope.ServiceProvider.GetRequiredService<RequestEmailChangeHandler>();

		(await handler.HandleAsync(userId, destino, IdentitySeed.Password, remoteAddress: null)).Should().BeTrue();

		factory.Emails.For(destino).Should().ContainSingle()
			.Which.Body.Should().Contain("/conta/confirmar-email?");

		var aviso = factory.Emails.For(atual).Should().ContainSingle().Subject;
		aviso.Body.Should().NotContain("http", "aviso com link seria um segundo alvo de phishing");
		aviso.Body.Should().NotContain(destino, "o endereço novo aparece mascarado");
	}

	[Fact]
	public async Task Pedido_ParaEmailEmUso_RespondeIgualENaoEnvia()
	{
		var (userId, _) = await UserAsync();
		var (_, ocupado) = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var handler = scope.ServiceProvider.GetRequiredService<RequestEmailChangeHandler>();

		// true significa "a senha conferiu", nunca "o endereço estava livre": quem chama não
		// consegue distinguir os dois casos (ADR-0020).
		(await handler.HandleAsync(userId, ocupado, IdentitySeed.Password, remoteAddress: null)).Should().BeTrue();
		factory.Emails.For(ocupado).Should().BeEmpty();
	}

	[Fact]
	public async Task Confirmacao_TrocaOEmailEEncerraAsSessoes()
	{
		var (userId, atual) = await UserAsync();
		var destino = Email();
		var driver = new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password);
		var (_, refreshToken) = await driver.LoginAsync(atual, "openid offline_access logstream");

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
		var confirm = scope.ServiceProvider.GetRequiredService<ConfirmEmailChangeHandler>();
		var token = await tokens.CreateEmailChangeTokenAsync(userId, destino);

		(await confirm.HandleAsync(userId, destino, token)).Should().Be(CredentialTokenOutcome.Done);

		(await driver.RefreshAsync(refreshToken)).StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
		(await new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password)
			.LoginAsync(destino, "openid offline_access logstream")).AccessToken.Should().NotBeNullOrEmpty();
	}

	[Fact]
	public async Task Confirmacao_MataOConvitePendenteDaConta()
	{
		var (userId, _) = await UserAsync();
		var destino = Email();

		using var scope = factory.Services.CreateScope();
		var tokens = scope.ServiceProvider.GetRequiredService<ICredentialTokens>();
		var confirm = scope.ServiceProvider.GetRequiredService<ConfirmEmailChangeHandler>();

		var conviteAntigo = await tokens.CreateInviteTokenAsync(userId);
		var token = await tokens.CreateEmailChangeTokenAsync(userId, destino);
		await confirm.HandleAsync(userId, destino, token);

		// A troca move o SecurityStamp, então todo link pendente morre junto.
		(await tokens.IsLinkValidAsync(userId, conviteAntigo, invite: true)).Should().BeFalse();
	}

	private async Task<HttpClient> SignedInBrowserAsync(string email, string password)
	{
		var browser = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
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

		login.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);

		return browser;
	}

	private static async Task<HttpResponseMessage> SubmitChangeAsync(HttpClient browser, string novoEmail, string senha)
	{
		var form = await browser.GetAsync("/conta/trocar-email");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await form.Content.ReadAsStringAsync());

		return await browser.PostAsync("/conta/trocar-email", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.NewEmail"] = novoEmail,
			["Input.CurrentPassword"] = senha,
		}));
	}

	private string ConfirmLinkFor(string email)
	{
		var link = ConfirmLink().Match(factory.Emails.For(email).Last().Body).Value;

		// A base do link vem da configuração, nunca do header Host (ADR-0020).
		link.Should().StartWith(SecureGateApiFactory.PublicBaseUrl);

		return link;
	}

	[GeneratedRegex(@"https://\S+/conta/confirmar-email\?\S+")]
	private static partial Regex ConfirmLink();

	[Fact]
	public async Task TelaDeTroca_SemSessao_RedirecionaParaOLogin()
	{
		using var anonimo = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
		});

		var response = await anonimo.GetAsync("/conta/trocar-email");

		response.StatusCode.Should().Be(System.Net.HttpStatusCode.Redirect);
		response.Headers.Location!.ToString().Should().Contain("/login");
	}

	[Fact]
	public async Task TelaDeTroca_PeloDono_ConfirmaEPermiteLogarComONovo()
	{
		var (_, atual) = await UserAsync();
		var destino = Email();
		using var browser = await SignedInBrowserAsync(atual, IdentitySeed.Password);

		var pedido = await SubmitChangeAsync(browser, destino, IdentitySeed.Password);

		pedido.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
		(await pedido.Content.ReadAsStringAsync()).Should().Contain("enviamos um link");

		using var novaJanela = factory.CreateClient();
		var confirmacao = await novaJanela.GetAsync(new Uri(ConfirmLinkFor(destino)).PathAndQuery);

		confirmacao.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
		(await confirmacao.Content.ReadAsStringAsync()).Should().Contain("E-mail confirmado");

		(await new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password)
			.LoginAsync(destino, "openid offline_access logstream")).AccessToken.Should().NotBeNullOrEmpty();
	}

	[Fact]
	public async Task TelaDeTroca_SenhaErrada_MostraOErroENaoEnvia()
	{
		var (_, atual) = await UserAsync();
		var destino = Email();
		using var browser = await SignedInBrowserAsync(atual, IdentitySeed.Password);

		var response = await SubmitChangeAsync(browser, destino, "Errada@Senha1");

		(await response.Content.ReadAsStringAsync()).Should().Contain("Senha atual incorreta");
		factory.Emails.For(destino).Should().BeEmpty();
	}

	[Fact]
	public async Task TelaDeTroca_EmailEmUso_RespondeExatamenteComoEmailLivre()
	{
		var (_, primeiro) = await UserAsync();
		var (_, ocupado) = await UserAsync();
		using var browser = await SignedInBrowserAsync(primeiro, IdentitySeed.Password);

		var livre = await SubmitChangeAsync(browser, Email(), IdentitySeed.Password);
		var emUso = await SubmitChangeAsync(browser, ocupado, IdentitySeed.Password);

		(await emUso.Content.ReadAsStringAsync()).Should().Be(await livre.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task Confirmacao_LinkJaUsado_RecusaNaSegundaAbertura()
	{
		var (_, atual) = await UserAsync();
		var destino = Email();
		using var browser = await SignedInBrowserAsync(atual, IdentitySeed.Password);
		await SubmitChangeAsync(browser, destino, IdentitySeed.Password);
		var caminho = new Uri(ConfirmLinkFor(destino)).PathAndQuery;

		using var janela = factory.CreateClient();
		await janela.GetAsync(caminho);
		var segunda = await janela.GetAsync(caminho);

		(await segunda.Content.ReadAsStringAsync()).Should().Contain("não vale mais");
	}
}
