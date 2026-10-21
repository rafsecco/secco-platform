using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Secco.SDK.RelyingParty.Tests;

/// <summary>Access token JWT de teste, assinado com chave descartável (a leitura do pacote não valida).</summary>
internal static class TestJwt
{
	public static readonly string[] PwdMfa = ["pwd", "mfa"];

	public static string Create(DateTimeOffset? authTime, object? amr, string scope = "openid logstream")
	{
		var claims = new Dictionary<string, object> { ["sub"] = "u1", ["scope"] = scope };

		if (authTime is not null)
		{
			claims["auth_time"] = authTime.Value.ToUnixTimeSeconds();
		}

		if (amr is not null)
		{
			claims["amr"] = amr;
		}

		return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
		{
			Claims = claims,
			SigningCredentials = new SigningCredentials(
				new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
				SecurityAlgorithms.HmacSha256),
		});
	}
}

/// <summary>
/// Autenticação da pessoa (ADR-0050): instante e métodos vêm do access token custodiado; token opaco ou sem as
/// claims dá "desconhecido" (fail-closed).
/// </summary>
public class SeccoUserAuthenticationTests
{
	private const string Id = RelyingPartyFixture.SessionId;

	private static SeccoUserSession Blank() => RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddMinutes(5));

	[Fact]
	public void Jwt_LeAuthTimeEAmrComVariosValores()
	{
		var at = DateTimeOffset.UtcNow.AddMinutes(-2);

		var session = Blank().WithAuthenticationOf(TestJwt.Create(at, TestJwt.PwdMfa));

		session.AuthenticatedAt.Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
		session.AuthenticationMethods.Should().Equal("pwd", "mfa");
	}

	[Fact]
	public void Jwt_AmrComUmValorSo()
	{
		var session = Blank().WithAuthenticationOf(TestJwt.Create(DateTimeOffset.UtcNow, "pwd"));

		session.AuthenticationMethods.Should().Equal("pwd");
	}

	[Fact]
	public void Jwt_SemAsClaims_FicaDesconhecido()
	{
		var session = Blank().WithAuthenticationOf(TestJwt.Create(null, null));

		session.AuthenticatedAt.Should().BeNull();
		session.AuthenticationMethods.Should().BeEmpty();
	}

	[Fact]
	public void TokenOpaco_FicaDesconhecido()
	{
		var session = Blank().WithAuthenticationOf("token-opaco");

		session.AuthenticatedAt.Should().BeNull();
		session.AuthenticationMethods.Should().BeEmpty();
	}

	[Fact]
	public async Task Cofre_GuardaAutenticacaoETokensTrocados_SemExporNoToString()
	{
		var fixture = new RelyingPartyFixture();
		var at = DateTimeOffset.UtcNow.AddMinutes(-1);
		var session = Blank().WithAuthenticationOf(TestJwt.Create(at, TestJwt.PwdMfa)) with
		{
			ExchangedTokens = new Dictionary<string, SeccoExchangedToken>
			{
				["logstream"] = new("x-secreto", DateTimeOffset.UtcNow.AddMinutes(2)),
			},
		};

		await fixture.Store.SetAsync(Id, session);
		var read = await fixture.Store.GetAsync(Id);

		read!.AuthenticatedAt.Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
		read.AuthenticationMethods.Should().Equal("pwd", "mfa");
		read.ExchangedTokens["logstream"].AccessToken.Should().Be("x-secreto");
		read.ExchangedTokens["logstream"].ToString().Should().NotContain("secreto");
	}

	[Fact]
	public async Task SessaoAntigaSemOsCamposNovos_DesserializaVazia()
	{
		// Entrada gravada por versão anterior do pacote (sem as propriedades novas)
		var fixture = new RelyingPartyFixture();
		var cache = fixture.Provider.GetRequiredService<IDistributedCache>();
		await cache.SetAsync(
			"secco:relyingparty:session:" + Id,
			System.Text.Encoding.UTF8.GetBytes(
				"""{"AccessToken":"a","RefreshToken":"r","ExpiresAt":"2030-01-01T00:00:00+00:00","Scopes":["openid"]}"""));

		var read = await fixture.Store.GetAsync(Id);

		read!.AuthenticatedAt.Should().BeNull();
		read.AuthenticationMethods.Should().BeEmpty();
		read.ExchangedTokens.Should().BeEmpty();
	}

	[Fact]
	public async Task Provedor_ExpoeAutenticacaoDaSessao()
	{
		var fixture = new RelyingPartyFixture();
		var at = DateTimeOffset.UtcNow.AddMinutes(-3);
		await fixture.Store.SetAsync(Id, Blank().WithAuthenticationOf(TestJwt.Create(at, TestJwt.PwdMfa)));

		var authentication = await fixture.NewAuthentication().GetAuthenticationAsync();

		authentication!.AuthenticatedAt.Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
		authentication.HasMethod("mfa").Should().BeTrue();
		authentication.HasMethod("otp").Should().BeFalse();
	}

	[Fact]
	public async Task Provedor_SemSessao_DevolveNulo()
	{
		var fixture = new RelyingPartyFixture();

		(await fixture.NewAuthentication().GetAuthenticationAsync()).Should().BeNull();
	}

	[Fact]
	public async Task Renovacao_LeAutenticacaoDoTokenNovo()
	{
		var fixture = new RelyingPartyFixture();
		var at = DateTimeOffset.UtcNow.AddMinutes(-4);
		var renewed = TestJwt.Create(at, TestJwt.PwdMfa);
		fixture.TokenEndpoint.Respond = _ => FakeTokenEndpoint.Json(
			$$"""{"access_token":"{{renewed}}","refresh_token":"r1","expires_in":300}""");
		await fixture.Store.SetAsync(Id, Blank() with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30) });

		var authentication = await fixture.NewAuthentication().GetAuthenticationAsync();

		authentication!.AuthenticatedAt.Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
		authentication.HasMethod("mfa").Should().BeTrue();
	}

	[Fact]
	public async Task TokenValidado_GravaAutenticacaoDoAccessToken()
	{
		var fixture = new RelyingPartyFixture();
		var at = DateTimeOffset.UtcNow.AddSeconds(-30);
		var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("oidc"));
		var oidc = fixture.Provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>>().Get("OpenIdConnect");
		var context = new Microsoft.AspNetCore.Authentication.OpenIdConnect.TokenValidatedContext(
			new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = fixture.Provider },
			new Microsoft.AspNetCore.Authentication.AuthenticationScheme("OpenIdConnect", null, typeof(Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectHandler)),
			oidc,
			principal,
			new Microsoft.AspNetCore.Authentication.AuthenticationProperties())
		{
			TokenEndpointResponse = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectMessage
			{
				AccessToken = TestJwt.Create(at, TestJwt.PwdMfa),
				RefreshToken = "r",
				ExpiresIn = "300",
			},
		};

		await oidc.Events.OnTokenValidated(context);

		var stored = await fixture.Store.GetAsync(principal.FindFirst("secco_session")!.Value);
		stored!.AuthenticatedAt.Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
		stored.AuthenticationMethods.Should().Equal("pwd", "mfa");
	}
}
