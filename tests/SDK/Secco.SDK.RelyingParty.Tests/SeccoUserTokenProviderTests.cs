using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Secco.SDK.RelyingParty.Tests;

/// <summary>
/// Token da pessoa no cofre (ADR-0046): o provedor entrega um token válido, renova perto do vencimento uma vez por
/// sessão guardando o refresh rotacionado, e manda para novo login quando a sessão se perde.
/// </summary>
public class SeccoUserTokenProviderTests
{
	private const string Id = RelyingPartyFixture.SessionId;

	[Fact]
	public async Task Cofre_GuardaELeDeVolta()
	{
		var fixture = new RelyingPartyFixture();
		var session = RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddMinutes(5));

		await fixture.Store.SetAsync(Id, session);

		var read = await fixture.Store.GetAsync(Id);
		read.Should().NotBeNull();
		read!.AccessToken.Should().Be("a0");
		read.RefreshToken.Should().Be("r0");
		read.ExpiresAt.Should().BeCloseTo(session.ExpiresAt, TimeSpan.FromSeconds(1));
		read.Scopes.Should().Equal("openid", "securegate:tenant-admin");

		await fixture.Store.RemoveAsync(Id);
		(await fixture.Store.GetAsync(Id)).Should().BeNull();
	}

	[Fact]
	public void Sessao_ToStringNaoExpoeTokens()
	{
		var text = RelyingPartyFixture.Session(DateTimeOffset.UtcNow, "token-secreto", "refresh-secreto").ToString();

		text.Should().NotContain("secreto");
	}

	[Fact]
	public async Task TokenValido_DevolveSemRenovar()
	{
		var fixture = new RelyingPartyFixture();
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddMinutes(4)));

		(await fixture.NewTokenProvider().GetAccessTokenAsync()).Should().Be("a0");
		fixture.TokenEndpoint.Calls.Should().Be(0);
	}

	[Fact]
	public async Task SemClaimDeSessao_DevolveNuloSemMandarParaLogin()
	{
		var fixture = new RelyingPartyFixture();
		fixture.UserContext.GetUserAsync().Returns(Task.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal(new ClaimsIdentity())));

		(await fixture.NewTokenProvider().GetAccessTokenAsync()).Should().BeNull();
		fixture.UserContext.DidNotReceive().RequestReauthentication();
	}

	[Fact]
	public async Task SessaoAusenteNoCofre_SinalizaNovoLogin()
	{
		var fixture = new RelyingPartyFixture();

		(await fixture.NewTokenProvider().GetAccessTokenAsync()).Should().BeNull();
		fixture.UserContext.Received(1).RequestReauthentication();
	}

	[Fact]
	public async Task PertoDoVencimento_RenovaEGravaORefreshRotacionado()
	{
		var fixture = new RelyingPartyFixture();
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddSeconds(30)));

		(await fixture.NewTokenProvider().GetAccessTokenAsync()).Should().Be("a1");

		var stored = await fixture.Store.GetAsync(Id);
		stored!.AccessToken.Should().Be("a1");
		stored.RefreshToken.Should().Be("r1");
		fixture.TokenEndpoint.Bodies.Single().Should()
			.Contain("grant_type=refresh_token").And.Contain("refresh_token=r0")
			.And.Contain("client_id=meu-app").And.Contain("client_secret=segredo-do-app");
	}

	[Fact]
	public async Task RenovacaoRecusada_RemoveSessaoESinalizaNovoLogin()
	{
		var fixture = new RelyingPartyFixture();
		fixture.TokenEndpoint.Respond = _ => FakeTokenEndpoint.Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddSeconds(30)));

		(await fixture.NewTokenProvider().GetAccessTokenAsync()).Should().BeNull();

		(await fixture.Store.GetAsync(Id)).Should().BeNull();
		fixture.UserContext.Received(1).RequestReauthentication();
	}

	[Fact]
	public async Task ChamadasSimultaneas_UmaRenovacao()
	{
		var fixture = new RelyingPartyFixture();
		fixture.TokenEndpoint.Delay = TimeSpan.FromMilliseconds(100);
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddSeconds(30)));

		// Escopos de DI distintos, como requisições/circuitos diferentes da mesma sessão
		var tokens = await Task.WhenAll(
			fixture.NewTokenProvider().GetAccessTokenAsync(),
			fixture.NewTokenProvider().GetAccessTokenAsync(),
			fixture.NewTokenProvider().GetAccessTokenAsync());

		tokens.Should().OnlyContain(token => token == "a1");
		fixture.TokenEndpoint.Calls.Should().Be(1);
	}

	[Fact]
	public async Task EscoposConcedidos_SaoExpostosEAtualizadosNaRenovacao()
	{
		var fixture = new RelyingPartyFixture();
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddMinutes(4)));
		var provider = fixture.NewTokenProvider();

		(await provider.GetGrantedScopesAsync()).Should().Equal("openid", "securegate:tenant-admin");
		(await provider.HasScopeAsync("securegate:tenant-admin")).Should().BeTrue();
		(await provider.HasScopeAsync("securegate:admin")).Should().BeFalse();

		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddSeconds(30)));

		// O token renovado traz os escopos da resposta do emissor ("openid securegate:admin")
		(await fixture.NewTokenProvider().HasScopeAsync("securegate:admin")).Should().BeTrue();
	}

	[Fact]
	public async Task Renovacao_SemEscopoNaResposta_NaoHerdaOsEscoposAnteriores()
	{
		// Concessão revogada (ADR-0044): o token novo não traz o escopo, e a resposta não repete "scope"
		var fixture = new RelyingPartyFixture();
		fixture.TokenEndpoint.Respond = _ => FakeTokenEndpoint.Json(
			"""{"access_token":"a1","refresh_token":"r1","expires_in":300}""");
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddSeconds(30)));

		(await fixture.NewTokenProvider().HasScopeAsync("securegate:tenant-admin"))
			.Should().BeFalse("escopo do token anterior não sobrevive à renovação");
	}

	[Fact]
	public async Task Renovacao_LeOsEscoposDoProprioAccessToken()
	{
		var fixture = new RelyingPartyFixture();
		var jwt = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken(
			"""{"sub":"pessoa","scope":"openid logstream"}""");
		fixture.TokenEndpoint.Respond = _ => FakeTokenEndpoint.Json(
			$$"""{"access_token":"{{jwt}}","refresh_token":"r1","expires_in":300,"scope":"openid securegate:tenant-admin"}""");
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddSeconds(30)));

		(await fixture.NewTokenProvider().GetGrantedScopesAsync())
			.Should().Equal(["openid", "logstream"], "o token é a fonte; a resposta só vale para token opaco");
	}

	[Fact]
	public async Task SemSessao_NaoHaEscopos()
	{
		var fixture = new RelyingPartyFixture();
		fixture.UserContext.GetUserAsync().Returns(Task.FromResult<ClaimsPrincipal?>(null));

		var provider = fixture.NewTokenProvider();

		(await provider.GetGrantedScopesAsync()).Should().BeEmpty();
		(await provider.HasScopeAsync("openid")).Should().BeFalse();
	}

	[Fact]
	public async Task Handler_AnexaOBearerDaPessoa()
	{
		var fixture = new RelyingPartyFixture();
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddMinutes(4)));
		var downstream = new CapturingHandler();
		var handler = new SeccoUserTokenHandler(fixture.NewTokenProvider()) { InnerHandler = downstream };

		using var invoker = new HttpMessageInvoker(handler);
		using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.test/x"), default);

		downstream.Authorization.Should().Be("Bearer a0");
	}

	[Fact]
	public async Task Handler_SemSessao_NaoAnexaHeader()
	{
		var fixture = new RelyingPartyFixture();
		fixture.UserContext.GetUserAsync().Returns(Task.FromResult<ClaimsPrincipal?>(null));
		var downstream = new CapturingHandler();
		var handler = new SeccoUserTokenHandler(fixture.NewTokenProvider()) { InnerHandler = downstream };

		using var invoker = new HttpMessageInvoker(handler);
		using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.test/x"), default);

		downstream.Authorization.Should().BeNull();
	}

	[Fact]
	public async Task AddSeccoUserToken_PluginaNoHttpClient()
	{
		var fixture = new RelyingPartyFixture(services =>
			services.AddHttpClient("api", c => c.BaseAddress = new Uri("https://api.test"))
				.AddSeccoUserToken()
				.ConfigurePrimaryHttpMessageHandler(() => new CapturingHandler()));
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddMinutes(4)));

		var http = fixture.Provider.GetRequiredService<IHttpClientFactory>().CreateClient("api");
		using var response = await http.GetAsync("/x");

		(await response.Content.ReadAsStringAsync()).Should().Be("Bearer a0");
	}

	private sealed class CapturingHandler : HttpMessageHandler
	{
		public string? Authorization { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Authorization = request.Headers.Authorization?.ToString();
			return Task.FromResult(new HttpResponseMessage { Content = new StringContent(Authorization ?? "") });
		}
	}
}
