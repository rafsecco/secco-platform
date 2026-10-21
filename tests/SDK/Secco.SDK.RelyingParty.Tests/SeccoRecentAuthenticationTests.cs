using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Secco.SDK.RelyingParty.Tests;

/// <summary>
/// Autenticação recente (ADR-0050): o requisito lê o access token da sessão; autenticação antiga ou desconhecida
/// desafia o OIDC com prompt=login/max_age voltando à mesma URL; recente sem mfa dá 403.
/// </summary>
public class SeccoRecentAuthenticationTests
{
	private const string Id = RelyingPartyFixture.SessionId;

	private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

	private sealed class RecordingResultHandler : IAuthorizationMiddlewareResultHandler
	{
		public bool Called { get; private set; }

		public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
		{
			Called = true;
			return Task.CompletedTask;
		}
	}

	private static RelyingPartyFixture NewFixture(Action<IServiceCollection>? customize = null) =>
		new(services =>
		{
			customize?.Invoke(services);
			services.AddAuthorization(o => o.AddPolicy("recente", p => p
				.RequireAuthenticatedUser()
				.RequireSeccoRecentAuthentication(MaxAge)));
		});

	private static async Task StoreAsync(RelyingPartyFixture fixture, DateTimeOffset? authTime, object? amr) =>
		await fixture.Store.SetAsync(
			Id,
			RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddMinutes(4)).WithAuthenticationOf(TestJwt.Create(authTime, amr)));

	/// <summary>Executa a política e entrega o resultado ao handler de resultado registrado, como o middleware.</summary>
	private static async Task<DefaultHttpContext> RunAsync(RelyingPartyFixture fixture, bool authenticated = true)
	{
		var http = new DefaultHttpContext { RequestServices = fixture.Provider.CreateScope().ServiceProvider };
		http.Request.PathBase = "/app";
		http.Request.Path = "/admin/operacao";
		http.Request.QueryString = new QueryString("?x=1");

		if (authenticated)
		{
			http.User = RelyingPartyFixture.PrincipalWithSession(Id);
		}

		var policy = await http.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync("recente");
		var evaluator = http.RequestServices.GetRequiredService<IPolicyEvaluator>();
		var authentication = authenticated
			? AuthenticateResult.Success(new AuthenticationTicket(http.User, "Cookies"))
			: AuthenticateResult.NoResult();
		var result = await evaluator.AuthorizeAsync(policy!, authentication, http, null);

		await http.RequestServices.GetRequiredService<IAuthorizationMiddlewareResultHandler>()
			.HandleAsync(_ => Task.CompletedTask, http, policy!, result);

		return http;
	}

	private static async Task ShouldChallengeAsync(RelyingPartyFixture fixture)
	{
		await fixture.Authentication.Received(1).ChallengeAsync(
			Arg.Any<HttpContext>(),
			"OpenIdConnect",
			Arg.Is<AuthenticationProperties>(p => p is OpenIdConnectChallengeProperties
				&& ((OpenIdConnectChallengeProperties)p).Prompt == "login"
				&& ((OpenIdConnectChallengeProperties)p).MaxAge == MaxAge
				&& p.RedirectUri == "/app/admin/operacao?x=1"));
	}

	[Fact]
	public async Task Recente_ComMfa_Passa()
	{
		var fixture = NewFixture();
		await StoreAsync(fixture, DateTimeOffset.UtcNow.AddMinutes(-1), TestJwt.PwdMfa);

		var http = await RunAsync(fixture);

		await fixture.Authentication.DidNotReceive().ChallengeAsync(Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>());
		await fixture.Authentication.DidNotReceive().ForbidAsync(Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>());
		http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
	}

	[Fact]
	public async Task Antiga_DesafiaComPromptLoginEMaxAgeVoltandoAMesmaUrl()
	{
		var fixture = NewFixture();
		await StoreAsync(fixture, DateTimeOffset.UtcNow.AddMinutes(-30), TestJwt.PwdMfa);

		await RunAsync(fixture);

		await ShouldChallengeAsync(fixture);
	}

	[Fact]
	public async Task RecenteSemMfa_E403_SemDesafio()
	{
		var fixture = NewFixture();
		await StoreAsync(fixture, DateTimeOffset.UtcNow.AddMinutes(-1), "pwd");

		await RunAsync(fixture);

		await fixture.Authentication.DidNotReceive().ChallengeAsync(Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>());
		await fixture.Authentication.Received(1).ForbidAsync(Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>());
	}

	[Fact]
	public async Task SemAuthTime_Desafia()
	{
		var fixture = NewFixture();
		await StoreAsync(fixture, null, TestJwt.PwdMfa);

		await RunAsync(fixture);

		await ShouldChallengeAsync(fixture);
	}

	[Fact]
	public async Task TokenOpaco_Desafia()
	{
		var fixture = NewFixture();
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddMinutes(4)));

		await RunAsync(fixture);

		await ShouldChallengeAsync(fixture);
	}

	[Fact]
	public async Task SemRequireMfa_AutenticacaoRecenteBasta()
	{
		var fixture = new RelyingPartyFixture(services =>
			services.AddAuthorization(o => o.AddPolicy("recente", p => p
				.RequireAuthenticatedUser()
				.RequireSeccoRecentAuthentication(MaxAge, requireMfa: false))));
		await StoreAsync(fixture, DateTimeOffset.UtcNow.AddMinutes(-1), "pwd");

		await RunAsync(fixture);

		await fixture.Authentication.DidNotReceive().ChallengeAsync(Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>());
		await fixture.Authentication.DidNotReceive().ForbidAsync(Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>());
	}

	[Fact]
	public async Task NaoAutenticado_ComportamentoPadrao_SemPromptLogin()
	{
		var fixture = NewFixture();

		await RunAsync(fixture, authenticated: false);

		// Desafio padrão (esquema padrão, sem as propriedades do pacote)
		await fixture.Authentication.Received(1).ChallengeAsync(
			Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Is<AuthenticationProperties?>(p => !(p is OpenIdConnectChallengeProperties)));
	}

	[Fact]
	public async Task HandlerDaAplicacao_E_Decorado_ENaoSubstituido()
	{
		var custom = new RecordingResultHandler();
		var fixture = NewFixture(services => services.AddSingleton<IAuthorizationMiddlewareResultHandler>(custom));
		await StoreAsync(fixture, DateTimeOffset.UtcNow.AddMinutes(-1), "pwd");

		await RunAsync(fixture);

		// Falha de mfa vai para o handler da aplicação, que continua valendo
		custom.Called.Should().BeTrue();
	}

	[Fact]
	public async Task HandlerDaAplicacao_NaoRecebeAAutenticacaoAntiga()
	{
		var custom = new RecordingResultHandler();
		var fixture = NewFixture(services => services.AddSingleton<IAuthorizationMiddlewareResultHandler>(custom));
		await StoreAsync(fixture, DateTimeOffset.UtcNow.AddMinutes(-30), TestJwt.PwdMfa);

		await RunAsync(fixture);

		custom.Called.Should().BeFalse();
		await ShouldChallengeAsync(fixture);
	}

	[Fact]
	public void Requisito_RejeitaIdadeNaoPositiva()
	{
		var act = () => new SeccoRecentAuthenticationRequirement(TimeSpan.Zero);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Theory]
	[InlineData("/app/admin", true)]
	[InlineData("/", true)]
	[InlineData("//evil.test/admin", false)]
	[InlineData(@"/\evil.test/admin", false)]
	[InlineData("evil.test/admin", false)]
	[InlineData("/\t/evil.test/admin", false)]
	[InlineData("/\n/evil.test/admin", false)]
	[InlineData("/ /evil.test/admin", false)]
	public void VoltaDoDesafio_SoParaCaminhoLocal(string path, bool local)
	{
		// "//host" e "/\host" o navegador lê como outro domínio depois do callback do OIDC (redirecionamento aberto)
		SeccoRecentAuthenticationResultHandler.IsLocal(path).Should().Be(local);
	}
}
