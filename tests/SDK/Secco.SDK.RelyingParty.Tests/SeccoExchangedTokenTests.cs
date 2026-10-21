using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Secco.SDK.RelyingParty.Tests;

/// <summary>
/// Token trocado por escopo (ADR-0050): a troca RFC 8693 usa o token da pessoa, o resultado fica no cofre da
/// sessão e sobrevive à renovação; a recusa é genérica e nada é guardado.
/// </summary>
public class SeccoExchangedTokenTests
{
	private const string Id = RelyingPartyFixture.SessionId;

	private static async Task<RelyingPartyFixture> NewFixtureAsync(DateTimeOffset? expiresAt = null)
	{
		var fixture = new RelyingPartyFixture();
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(4)));
		return fixture;
	}

	[Fact]
	public async Task Troca_EmiteEEnviaOsCamposCertos()
	{
		var fixture = await NewFixtureAsync();

		var result = await fixture.NewExchangedProvider().GetAsync("logstream");

		result.Status.Should().Be(SeccoExchangeStatus.Issued);
		result.AccessToken.Should().Be("x1");
		result.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(120), TimeSpan.FromSeconds(5));

		var body = Uri.UnescapeDataString(fixture.TokenEndpoint.Bodies.Single());
		body.Should().Contain("grant_type=urn:ietf:params:oauth:grant-type:token-exchange")
			.And.Contain("subject_token=a0")
			.And.Contain("subject_token_type=urn:ietf:params:oauth:token-type:access_token")
			.And.Contain("requested_token_type=urn:ietf:params:oauth:token-type:access_token")
			.And.Contain("scope=logstream")
			.And.Contain("client_id=meu-app").And.Contain("client_secret=segredo-do-app");
	}

	[Fact]
	public async Task SegundaChamada_VemDoCofreSemNovaTroca()
	{
		var fixture = await NewFixtureAsync();

		var first = await fixture.NewExchangedProvider().GetAsync("logstream");
		var second = await fixture.NewExchangedProvider().GetAsync("logstream");

		second.AccessToken.Should().Be(first.AccessToken);
		fixture.TokenEndpoint.ExchangeCalls.Should().Be(1);
		(await fixture.Store.GetAsync(Id))!.ExchangedTokens.Should().ContainKey("logstream");
	}

	[Fact]
	public async Task EscoposDiferentes_TrocamSeparado()
	{
		var fixture = await NewFixtureAsync();
		var provider = fixture.NewExchangedProvider();

		await provider.GetAsync("logstream");
		await provider.GetAsync("securegate:admin");

		fixture.TokenEndpoint.ExchangeCalls.Should().Be(2);
		(await fixture.Store.GetAsync(Id))!.ExchangedTokens.Keys.Should().BeEquivalentTo("logstream", "securegate:admin");
	}

	[Fact]
	public async Task Recusa400_DaRefused_SemGuardarNada()
	{
		var fixture = await NewFixtureAsync();
		fixture.TokenEndpoint.RespondExchange = _ => FakeTokenEndpoint.Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);

		var result = await fixture.NewExchangedProvider().GetAsync("securegate:admin");

		result.Status.Should().Be(SeccoExchangeStatus.Refused);
		result.AccessToken.Should().BeNull();
		(await fixture.Store.GetAsync(Id))!.ExchangedTokens.Should().BeEmpty();
	}

	[Fact]
	public async Task Recusa401_DaRefused()
	{
		var fixture = await NewFixtureAsync();
		fixture.TokenEndpoint.RespondExchange = _ => FakeTokenEndpoint.Json("{}", HttpStatusCode.Unauthorized);

		(await fixture.NewExchangedProvider().GetAsync("logstream")).Status.Should().Be(SeccoExchangeStatus.Refused);
	}

	[Fact]
	public async Task FalhaDeInfraestrutura_Lanca_NaoViraRecusa()
	{
		var fixture = await NewFixtureAsync();
		fixture.TokenEndpoint.RespondExchange = _ => FakeTokenEndpoint.Json("{}", HttpStatusCode.InternalServerError);

		var act = () => fixture.NewExchangedProvider().GetAsync("logstream");

		await act.Should().ThrowAsync<HttpRequestException>();
	}

	[Fact]
	public async Task TokenTrocadoVencido_TrocaDeNovo()
	{
		var fixture = await NewFixtureAsync();
		var session = (await fixture.Store.GetAsync(Id))! with
		{
			ExchangedTokens = new Dictionary<string, SeccoExchangedToken>
			{
				["logstream"] = new("velho", DateTimeOffset.UtcNow.AddSeconds(-5)),
				["outro"] = new("outro-velho", DateTimeOffset.UtcNow.AddSeconds(-5)),
			},
		};
		await fixture.Store.SetAsync(Id, session);

		var result = await fixture.NewExchangedProvider().GetAsync("logstream");

		result.AccessToken.Should().Be("x1");
		fixture.TokenEndpoint.ExchangeCalls.Should().Be(1);
		(await fixture.Store.GetAsync(Id))!.ExchangedTokens.Keys.Should().Equal(["logstream"], "o vencido de outro escopo foi podado");
	}

	[Fact]
	public async Task TokenTrocadoDentroDaMargem_TrocaDeNovo()
	{
		var fixture = await NewFixtureAsync();
		var session = (await fixture.Store.GetAsync(Id))! with
		{
			ExchangedTokens = new Dictionary<string, SeccoExchangedToken>
			{
				["logstream"] = new("quase", DateTimeOffset.UtcNow.AddSeconds(20)),
			},
		};
		await fixture.Store.SetAsync(Id, session);

		(await fixture.NewExchangedProvider().GetAsync("logstream")).AccessToken.Should().Be("x1");
	}

	[Fact]
	public async Task TokensTrocados_SobrevivemARenovacaoDoTokenDaPessoa()
	{
		var fixture = await NewFixtureAsync();
		await fixture.NewExchangedProvider().GetAsync("logstream");

		// A pessoa chega perto do vencimento: o token é renovado e o trocado permanece
		var stored = (await fixture.Store.GetAsync(Id))! with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30) };
		await fixture.Store.SetAsync(Id, stored);
		fixture.TokenEndpoint.Respond = _ => FakeTokenEndpoint.Json("""{"access_token":"a-novo","refresh_token":"r-novo","expires_in":300}""");

		(await fixture.NewTokenProvider().GetAccessTokenAsync()).Should().Be("a-novo");

		var renewed = await fixture.Store.GetAsync(Id);
		renewed!.AccessToken.Should().Be("a-novo");
		renewed.ExchangedTokens["logstream"].AccessToken.Should().Be("x1");
		(await fixture.NewExchangedProvider().GetAsync("logstream")).AccessToken.Should().Be("x1");
		fixture.TokenEndpoint.ExchangeCalls.Should().Be(1);
	}

	[Fact]
	public async Task TokenDaPessoaVencendo_RenovaAntesDeTrocar()
	{
		var fixture = await NewFixtureAsync(DateTimeOffset.UtcNow.AddSeconds(30));
		fixture.TokenEndpoint.Respond = _ => FakeTokenEndpoint.Json("""{"access_token":"a-novo","refresh_token":"r-novo","expires_in":300}""");

		await fixture.NewExchangedProvider().GetAsync("logstream");

		Uri.UnescapeDataString(fixture.TokenEndpoint.Bodies.Last()).Should().Contain("subject_token=a-novo");
	}

	[Fact]
	public async Task SemSessaoNoCofre_NoSession()
	{
		var fixture = new RelyingPartyFixture();

		var result = await fixture.NewExchangedProvider().GetAsync("logstream");

		result.Status.Should().Be(SeccoExchangeStatus.NoSession);
		fixture.TokenEndpoint.ExchangeCalls.Should().Be(0);
	}

	[Fact]
	public async Task Anonimo_NoSession()
	{
		var fixture = new RelyingPartyFixture();
		fixture.UserContext.GetUserAsync().Returns(Task.FromResult<System.Security.Claims.ClaimsPrincipal?>(null));

		(await fixture.NewExchangedProvider().GetAsync("logstream")).Status.Should().Be(SeccoExchangeStatus.NoSession);
	}

	[Theory]
	[InlineData("")]
	[InlineData("a b")]
	[InlineData("a\tb")]
	[InlineData("a\"b")]
	[InlineData("a\nb")]
	public async Task EscopoInvalido_Lanca(string scope)
	{
		var fixture = await NewFixtureAsync();

		var act = () => fixture.NewExchangedProvider().GetAsync(scope);

		await act.Should().ThrowAsync<ArgumentException>();
		fixture.TokenEndpoint.ExchangeCalls.Should().Be(0);
	}

	[Fact]
	public async Task TrocasSimultaneasDoMesmoEscopo_UmaChamada()
	{
		var fixture = await NewFixtureAsync();
		fixture.TokenEndpoint.Delay = TimeSpan.FromMilliseconds(100);

		var results = await Task.WhenAll(
			fixture.NewExchangedProvider().GetAsync("logstream"),
			fixture.NewExchangedProvider().GetAsync("logstream"),
			fixture.NewExchangedProvider().GetAsync("logstream"));

		results.Should().OnlyContain(r => r.AccessToken == "x1");
		fixture.TokenEndpoint.ExchangeCalls.Should().Be(1);
	}

	[Fact]
	public void Resultado_ToStringNaoExpoeToken()
	{
		SeccoExchangeResult.Issued("token-secreto", DateTimeOffset.UtcNow).ToString().Should().NotContain("secreto");
	}

	// Handler

	private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(respond(request));
	}

	[Fact]
	public async Task Handler_AnexaOTokenTrocado_SubstituindoOutroBearer()
	{
		var fixture = await NewFixtureAsync();
		string? authorization = null;
		var handler = new SeccoExchangedTokenHandler(fixture.NewExchangedProvider(), "logstream")
		{
			InnerHandler = new StubHandler(request =>
			{
				authorization = request.Headers.Authorization?.ToString();
				return new HttpResponseMessage(HttpStatusCode.OK);
			}),
		};
		using var invoker = new HttpMessageInvoker(handler);
		using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test/x");
		request.Headers.TryAddWithoutValidation("Authorization", "Bearer da-pessoa");

		using var response = await invoker.SendAsync(request, CancellationToken.None);

		authorization.Should().Be("Bearer x1");
	}

	[Fact]
	public async Task Handler_TrocaRecusada_LancaReautenticacao_SemEnviar()
	{
		var fixture = await NewFixtureAsync();
		fixture.TokenEndpoint.RespondExchange = _ => FakeTokenEndpoint.Json("{}", HttpStatusCode.BadRequest);
		var sent = false;
		var handler = new SeccoExchangedTokenHandler(fixture.NewExchangedProvider(), "securegate:admin")
		{
			InnerHandler = new StubHandler(_ =>
			{
				sent = true;
				return new HttpResponseMessage(HttpStatusCode.OK);
			}),
		};
		using var invoker = new HttpMessageInvoker(handler);
		using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test/x");

		var act = () => invoker.SendAsync(request, CancellationToken.None);

		var thrown = await act.Should().ThrowAsync<SeccoReauthenticationRequiredException>();
		thrown.Which.Scope.Should().Be("securegate:admin");
		thrown.Which.Status.Should().Be(SeccoExchangeStatus.Refused);
		thrown.Which.Message.Should().NotContain("a0");
		sent.Should().BeFalse();
	}

	[Fact]
	public async Task Handler_SemSessao_LancaReautenticacao()
	{
		var fixture = new RelyingPartyFixture();
		var handler = new SeccoExchangedTokenHandler(fixture.NewExchangedProvider(), "logstream")
		{
			InnerHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
		};
		using var invoker = new HttpMessageInvoker(handler);
		using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test/x");

		var act = () => invoker.SendAsync(request, CancellationToken.None);

		(await act.Should().ThrowAsync<SeccoReauthenticationRequiredException>()).Which.Status.Should().Be(SeccoExchangeStatus.NoSession);
	}

	[Fact]
	public async Task AddSeccoExchangedToken_RegistraOHandlerNoHttpClient()
	{
		var fixture = new RelyingPartyFixture(services =>
			services.AddHttpClient("api").AddSeccoExchangedToken("logstream"));
		await fixture.Store.SetAsync(Id, RelyingPartyFixture.Session(DateTimeOffset.UtcNow.AddMinutes(4)));
		fixture.TokenEndpoint.RespondExchange = _ => FakeTokenEndpoint.Json("{}", HttpStatusCode.BadRequest);

		var client = fixture.Provider.CreateScope().ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("api");
		var act = () => client.GetAsync("https://api.test/x");

		await act.Should().ThrowAsync<SeccoReauthenticationRequiredException>();
	}
}
