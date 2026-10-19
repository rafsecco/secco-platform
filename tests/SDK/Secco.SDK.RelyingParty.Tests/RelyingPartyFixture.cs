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

	public static ClaimsPrincipal PrincipalWithSession(string sessionId) =>
		new(new ClaimsIdentity([new Claim("secco_session", sessionId)], "test"));

	public static SeccoUserSession Session(DateTimeOffset expiresAt, string access = "a0", string refresh = "r0") =>
		new(access, refresh, expiresAt, ["openid", "securegate:tenant-admin"]);
}
