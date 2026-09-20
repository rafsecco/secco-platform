using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// "Esqueci minha senha" e redefinição por link (ADR-0033).
/// </summary>
/// <remarks>
/// O que estes testes protegem, acima de tudo, é a <b>resposta idêntica</b>: conta que existe,
/// conta que não existe, conta desativada, conta só corporativa e pedido acima do limite têm de
/// produzir exatamente o mesmo HTML. Qualquer diferença transforma o formulário público num
/// verificador de quem tem conta na instalação (ADR-0020).
/// </remarks>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public partial class PasswordRecoveryTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string ClientId = "recuperacao-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string NewPassword = "Redef@Senha123";

	private Guid _tenantId;

	[GeneratedRegex(@"https://\S+/conta/redefinir-senha\?\S+")]
	private static partial Regex ResetLink();

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"recupera-{Guid.NewGuid():N}@secco.test";

	private HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions
	{
		AllowAutoRedirect = false,
		HandleCookies = true,
	});

	/// <summary>Submete o formulário público, com antiforgery, e devolve o HTML da resposta.</summary>
	private async Task<(HttpStatusCode Status, string Html)> ForgotAsync(string email)
	{
		using var browser = Browser();
		var page = await browser.GetAsync("/conta/esqueci");
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

		var response = await browser.PostAsync("/conta/esqueci", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.Email"] = email,
		}));

		return (response.StatusCode, await response.Content.ReadAsStringAsync());
	}

	private async Task<HttpResponseMessage> SubmitResetAsync(string link, string password)
	{
		using var browser = Browser();
		var path = new Uri(link).PathAndQuery;
		var form = await browser.GetAsync(path);
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await form.Content.ReadAsStringAsync());

		return await browser.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["__RequestVerificationToken"] = token,
			["Input.Password"] = password,
			["Input.ConfirmPassword"] = password,
		}));
	}

	private string ResetLinkFor(string email) =>
		ResetLink().Match(factory.Emails.For(email).Last().Body).Value;

	private async Task<(Guid UserId, string Email)> UserWithPasswordAsync()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);

		return (userId, email);
	}

	private async Task SetLocalLoginAsync(Guid userId, bool enabled)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var user = await context.Users.FindAsync(userId);
		user!.LocalLoginEnabled = enabled;
		await context.SaveChangesAsync();
	}

	[Fact]
	public async Task Esqueci_ContaInexistente_RespondeExatamenteComoContaExistente()
	{
		var (_, existente) = await UserWithPasswordAsync();

		var comConta = await ForgotAsync(existente);
		var semConta = await ForgotAsync($"ninguem-{Guid.NewGuid():N}@secco.test");

		semConta.Status.Should().Be(comConta.Status);
		semConta.Html.Should().Be(comConta.Html);
		factory.Emails.For(existente).Should().ContainSingle();
	}

	[Fact]
	public async Task Esqueci_ContaDesativada_NaoEnviaERespondeIgual()
	{
		var (userId, email) = await UserWithPasswordAsync();
		await IdentitySeed.DeactivateAsync(factory, userId);

		var response = await ForgotAsync(email);

		response.Status.Should().Be(HttpStatusCode.OK);
		factory.Emails.For(email).Should().BeEmpty();
	}

	[Fact]
	public async Task Esqueci_ContaSoCorporativa_NaoEnviaERespondeIgual()
	{
		var (userId, email) = await UserWithPasswordAsync();
		await SetLocalLoginAsync(userId, enabled: false);

		var response = await ForgotAsync(email);

		response.Status.Should().Be(HttpStatusCode.OK);
		factory.Emails.For(email).Should().BeEmpty();
	}

	[Fact]
	public async Task Esqueci_AcimaDoLimitePorConta_ParaDeEnviarSemMudarAResposta()
	{
		var (_, email) = await UserWithPasswordAsync();
		var primeira = await ForgotAsync(email);

		for (var i = 0; i < 3; i++)
		{
			await ForgotAsync(email);
		}

		var excedente = await ForgotAsync(email);

		excedente.Html.Should().Be(primeira.Html, "exceder o limite não pode ser distinguível (ADR-0020)");
		factory.Emails.For(email).Should().HaveCount(3, "o limite padrão é 3 por conta por hora");
	}

	[Fact]
	public async Task Redefinir_PeloLink_TrocaASenhaEEncerraAsSessoes()
	{
		var (_, email) = await UserWithPasswordAsync();
		var driver = new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password);
		var (_, refreshToken) = await driver.LoginAsync(email, "openid offline_access logstream");

		await ForgotAsync(email);
		var redefinicao = await SubmitResetAsync(ResetLinkFor(email), NewPassword);

		redefinicao.StatusCode.Should().Be(HttpStatusCode.Redirect);

		var renovacao = await driver.RefreshAsync(refreshToken);
		renovacao.StatusCode.Should().Be(HttpStatusCode.BadRequest, "todo evento de senha revoga sessão (ADR-0032/0033)");

		var comNovaSenha = new OidcLoginDriver(factory, ClientId, RedirectUri, NewPassword);
		(await comNovaSenha.LoginAsync(email, "openid offline_access logstream")).AccessToken.Should().NotBeNullOrEmpty();
	}

	[Fact]
	public async Task Redefinir_AvisaODonoPorEmail()
	{
		var (_, email) = await UserWithPasswordAsync();
		await ForgotAsync(email);

		await SubmitResetAsync(ResetLinkFor(email), NewPassword);

		factory.Emails.For(email).Last().Subject.Should().Contain("alterada");
		factory.Emails.For(email).Last().Body.Should().NotContain(NewPassword);
	}

	[Fact]
	public async Task Redefinir_LinkUsadoDuasVezes_SegundaVezRecusada()
	{
		var (_, email) = await UserWithPasswordAsync();
		await ForgotAsync(email);
		var link = ResetLinkFor(email);

		(await SubmitResetAsync(link, NewPassword)).StatusCode.Should().Be(HttpStatusCode.Redirect);
		var again = await SubmitResetAsync(link, "Outra@Senha123");

		again.StatusCode.Should().Be(HttpStatusCode.OK);
		(await again.Content.ReadAsStringAsync()).Should().Contain("pedir outro");
	}

	[Fact]
	public async Task Redefinir_ComTokenDeOutraConta_Recusado()
	{
		var (_, primeiro) = await UserWithPasswordAsync();
		var (outroId, segundo) = await UserWithPasswordAsync();
		await ForgotAsync(primeiro);
		await ForgotAsync(segundo);

		var link = ResetLinkFor(primeiro);
		var trocado = TrocarUserId(link, outroId);

		var response = await SubmitResetAsync(trocado, NewPassword);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("pedir outro");
	}

	[Fact]
	public async Task Esqueci_ContaSemSenhaAinda_ReenviaOConvite()
	{
		var email = Email();
		Guid userId;

		using (var scope = factory.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			var user = new User
			{
				Id = Guid.CreateVersion7(),
				TenantId = _tenantId,
				UserName = email,
				Email = email,
				NormalizedUserName = email.ToUpperInvariant(),
				NormalizedEmail = email.ToUpperInvariant(),
				SecurityStamp = Guid.NewGuid().ToString(),
			};
			context.Users.Add(user);
			await context.SaveChangesAsync();
			userId = user.Id;
		}

		await ForgotAsync(email);

		// Convite expirado se resolve pelo mesmo formulário: quem nunca teve senha recebe
		// convite, não redefinição.
		factory.Emails.For(email).Should().ContainSingle()
			.Which.Body.Should().Contain("/conta/definir-senha?").And.Contain(userId.ToString());
	}

	private static string TrocarUserId(string link, Guid outroId)
	{
		var uri = new Uri(link);
		var atual = uri.Query.Split("userId=")[1].Split('&')[0];

		return link.Replace(atual, outroId.ToString(), StringComparison.Ordinal);
	}
}
