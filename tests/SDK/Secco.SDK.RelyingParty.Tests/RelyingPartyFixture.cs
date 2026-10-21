using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NSubstitute;

namespace Secco.SDK.RelyingParty.Tests;

/// <summary>Token endpoint falso: grava as chamadas e responde com o que o teste programar.</summary>
internal sealed class FakeTokenEndpoint : HttpMessageHandler
{
	private int _calls;

	public int Calls => _calls;

	public List<string> Bodies { get; } = [];

	public TimeSpan Delay { get; set; } = TimeSpan.Zero;

	public Func<int, HttpResponseMessage> Respond { get; set; } = n => Json(
		$$"""{"access_token":"a{{n + 1}}","refresh_token":"r{{n + 1}}","expires_in":300,"scope":"openid securegate:admin"}""");

	/// <summary>Chamadas de troca (RFC 8693); as demais são renovações.</summary>
	public int ExchangeCalls { get; private set; }

	/// <summary>Resposta às trocas; o parâmetro é o número da troca (0, 1…).</summary>
	public Func<int, HttpResponseMessage> RespondExchange { get; set; } = n => Json(
		$$"""{"access_token":"x{{n + 1}}","token_type":"Bearer","expires_in":120,"scope":"logstream"}""");

	public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
		new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var number = Interlocked.Increment(ref _calls) - 1;
		var body = await request.Content!.ReadAsStringAsync(cancellationToken);

		lock (Bodies)
		{
			Bodies.Add(body);
		}

		if (Delay > TimeSpan.Zero)
		{
			await Task.Delay(Delay, cancellationToken);
		}

		if (body.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Atoken-exchange", StringComparison.Ordinal))
		{
			int exchange;

			lock (Bodies)
			{
				exchange = ExchangeCalls++;
			}

			return RespondExchange(exchange);
		}

		return Respond(number);
	}
}

/// <summary>Composição real do pacote sobre cookie + OIDC com token endpoint falso.</summary>
internal sealed class RelyingPartyFixture
{
	public const string SessionId = "sessao-1";

	public RelyingPartyFixture(Action<IServiceCollection>? customize = null)
	{
		var services = new ServiceCollection();
		services.AddLogging();

		services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
			.AddCookie()
			.AddOpenIdConnect(options =>
			{
				options.ClientId = "meu-app";
				options.ClientSecret = "segredo-do-app";
				options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
				options.Configuration = new OpenIdConnectConfiguration { TokenEndpoint = "https://idp.test/connect/token" };
				options.Backchannel = new HttpClient(TokenEndpoint);
			});

		customize?.Invoke(services);
		services.AddSingleton(UserContext);
		services.AddSeccoRelyingPartyTokens();
		services.AddSingleton(Authentication);

		Provider = services.BuildServiceProvider();
		UserContext.GetUserAsync().Returns(Task.FromResult<ClaimsPrincipal?>(PrincipalWithSession(SessionId)));
	}

	public FakeTokenEndpoint TokenEndpoint { get; } = new();

	public ISeccoUserContext UserContext { get; } = Substitute.For<ISeccoUserContext>();

	public IAuthenticationService Authentication { get; } = Substitute.For<IAuthenticationService>();

	public ServiceProvider Provider { get; }

	public ISeccoUserSessionStore Store => Provider.GetRequiredService<ISeccoUserSessionStore>();

	public ISeccoUserTokenProvider NewTokenProvider() =>
		Provider.CreateScope().ServiceProvider.GetRequiredService<ISeccoUserTokenProvider>();

	public ISeccoUserAuthentication NewAuthentication() =>
		Provider.CreateScope().ServiceProvider.GetRequiredService<ISeccoUserAuthentication>();

	public ISeccoExchangedTokenProvider NewExchangedProvider() =>
		Provider.CreateScope().ServiceProvider.GetRequiredService<ISeccoExchangedTokenProvider>();

	public static ClaimsPrincipal PrincipalWithSession(string sessionId) =>
		new(new ClaimsIdentity([new Claim("secco_session", sessionId)], "test"));

	public static SeccoUserSession Session(DateTimeOffset expiresAt, string access = "a0", string refresh = "r0") =>
		new(access, refresh, expiresAt, ["openid", "securegate:tenant-admin"]);
}
