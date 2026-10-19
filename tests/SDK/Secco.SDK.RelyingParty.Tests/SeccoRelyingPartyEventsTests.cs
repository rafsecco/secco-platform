using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NSubstitute;
using Xunit;

namespace Secco.SDK.RelyingParty.Tests;

/// <summary>
/// Os eventos do cookie e do OIDC: o token vai para o cofre e só o id da sessão para o cookie; cookie cuja
/// sessão não está no cofre é rejeitado; eventos da aplicação seguem valendo.
/// </summary>
public class SeccoRelyingPartyEventsTests
{
	private static readonly AuthenticationScheme OidcScheme = new("OpenIdConnect", null, typeof(OpenIdConnectHandler));

	private static readonly AuthenticationScheme CookieScheme = new("Cookies", null, typeof(CookieAuthenticationHandler));

	private static DefaultHttpContext Http(RelyingPartyFixture fixture) =>
		new DefaultHttpContext { RequestServices = fixture.Provider };

	private static OpenIdConnectOptions Oidc(RelyingPartyFixture fixture) =>
		fixture.Provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("OpenIdConnect");

	private static CookieAuthenticationOptions Cookie(RelyingPartyFixture fixture) =>
		fixture.Provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");

	private static TokenValidatedContext TokenContext(RelyingPartyFixture fixture, OpenIdConnectMessage? tokens, ClaimsPrincipal principal) =>
		new(Http(fixture), OidcScheme, Oidc(fixture), principal, new AuthenticationProperties())
		{
			TokenEndpointResponse = tokens,
		};

	[Fact]
	public async Task TokenValidado_VaiParaOCofreEOCookieLevaSoOIdDaSessao()
	{
		var fixture = new RelyingPartyFixture();
		var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], "oidc"));
		var context = TokenContext(fixture, new OpenIdConnectMessage
		{
			AccessToken = "access-secreto",
			RefreshToken = "refresh-secreto",
			ExpiresIn = "300",
			Scope = "openid securegate:tenant-admin",
		}, principal);

		await Oidc(fixture).Events.OnTokenValidated(context);

		var sessionId = principal.FindFirst("secco_session")!.Value;
		sessionId.Should().NotBeNullOrEmpty();
		principal.Claims.Select(c => c.Value).Should().NotContain(v => v.Contains("secreto"));

		var stored = await fixture.Store.GetAsync(sessionId);
		stored!.AccessToken.Should().Be("access-secreto");
		stored.RefreshToken.Should().Be("refresh-secreto");
		stored.Scopes.Should().Equal("openid", "securegate:tenant-admin");
	}

	[Fact]
	public async Task TokenValidado_SemRefreshToken_FalhaOLogin()
	{
		var fixture = new RelyingPartyFixture();
		var context = TokenContext(fixture, new OpenIdConnectMessage { AccessToken = "a" },
			new ClaimsPrincipal(new ClaimsIdentity("oidc")));

		await Oidc(fixture).Events.OnTokenValidated(context);

		context.Result.Should().NotBeNull();
		context.Result!.Failure.Should().NotBeNull();
	}

	[Fact]
	public async Task EventoDaAplicacao_ContinuaValendoEEncadeia()
	{
		var applicationRan = false;
		var fixture = new RelyingPartyFixture(services =>
			services.Configure<OpenIdConnectOptions>("OpenIdConnect", o => o.Events.OnTokenValidated = _ =>
			{
				applicationRan = true;
				return Task.CompletedTask;
			}));
		var principal = new ClaimsPrincipal(new ClaimsIdentity("oidc"));
		var context = TokenContext(fixture, new OpenIdConnectMessage { AccessToken = "a", RefreshToken = "r" }, principal);

		await Oidc(fixture).Events.OnTokenValidated(context);

		applicationRan.Should().BeTrue();
		principal.FindFirst("secco_session").Should().NotBeNull();
	}

	private static CookieValidatePrincipalContext CookieContext(RelyingPartyFixture fixture, ClaimsPrincipal principal) =>
		new(Http(fixture), CookieScheme, Cookie(fixture), new AuthenticationTicket(principal, "Cookies"));

	[Fact]
	public async Task Cookie_ComSessaoNoCofre_Passa()
	{
		var fixture = new RelyingPartyFixture();
		await fixture.Store.SetAsync(RelyingPartyFixture.SessionId, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddMinutes(5)));
		var context = CookieContext(fixture, RelyingPartyFixture.PrincipalWithSession(RelyingPartyFixture.SessionId));

		await Cookie(fixture).Events.OnValidatePrincipal(context);

		context.Principal.Should().NotBeNull();
	}

	[Fact]
	public async Task Cookie_SemSessaoNoCofre_ERejeitado()
	{
		var fixture = new RelyingPartyFixture();
		var context = CookieContext(fixture, RelyingPartyFixture.PrincipalWithSession("some-do-cofre"));

		await Cookie(fixture).Events.OnValidatePrincipal(context);

		context.Principal.Should().BeNull();
		await fixture.Authentication.Received(1).SignOutAsync(Arg.Any<HttpContext>(), "Cookies", Arg.Any<AuthenticationProperties?>());
	}

	[Fact]
	public async Task Cookie_SemClaimDeSessao_ERejeitado()
	{
		var fixture = new RelyingPartyFixture();
		var context = CookieContext(fixture, new ClaimsPrincipal(new ClaimsIdentity("test")));

		await Cookie(fixture).Events.OnValidatePrincipal(context);

		context.Principal.Should().BeNull();
	}
}
