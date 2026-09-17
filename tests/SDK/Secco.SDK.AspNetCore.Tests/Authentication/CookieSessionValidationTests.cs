using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Secco.SDK.AspNetCore.Authentication;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Authentication;

/// <summary>A sessão local de uma aplicação de cookie cai quando a sessão é revogada (ADR-0032).</summary>
public class CookieSessionValidationTests
{
	private static async Task<IHost> StartAsync(ISessionVersionResolver? resolver)
	{
		return await new HostBuilder()
			.ConfigureWebHost(web =>
			{
				web.UseTestServer();
				web.ConfigureServices(services =>
				{
					services.AddRouting();
					services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
					services.AddAuthorization();

					if (resolver is not null)
					{
						services.AddSingleton(resolver);
					}

					services.AddSeccoCookieSessionValidation(CookieAuthenticationDefaults.AuthenticationScheme);
				});
				web.Configure(app =>
				{
					app.UseRouting();
					app.UseAuthentication();
					app.UseAuthorization();
					app.UseEndpoints(endpoints =>
					{
						endpoints.MapGet("/entrar", async (HttpContext context) =>
						{
							var identity = new ClaimsIdentity(
								[new Claim(SeccoClaims.Subject, "user-1"), new Claim(SeccoClaims.SessionVersion, "v1")],
								CookieAuthenticationDefaults.AuthenticationScheme);
							await context.SignInAsync(new ClaimsPrincipal(identity));
						});
						endpoints.MapGet("/perfil", (ClaimsPrincipal user) => user.Identity!.IsAuthenticated ? "logado" : "anonimo");
					});
				});
			})
			.StartAsync();
	}

	private static async Task<string> SignInAndReadProfileAsync(IHost host)
	{
		var client = host.GetTestClient();
		var signIn = await client.GetAsync("/entrar");
		var cookie = signIn.Headers.GetValues("Set-Cookie").First().Split(';')[0];

		using var request = new HttpRequestMessage(HttpMethod.Get, "/perfil");
		request.Headers.Add("Cookie", cookie);

		return await (await client.SendAsync(request)).Content.ReadAsStringAsync();
	}

	[Fact]
	public async Task VersaoIgual_ContinuaLogado()
	{
		using var host = await StartAsync(new SessionVersionValidationTests.FakeResolver());

		(await SignInAndReadProfileAsync(host)).Should().Be("logado");
	}

	[Fact]
	public async Task VersaoDiferente_CookieRejeitado()
	{
		using var host = await StartAsync(new SessionVersionValidationTests.FakeResolver { Status = new("v2", false) });

		(await SignInAndReadProfileAsync(host)).Should().Be("anonimo");
	}

	[Fact]
	public async Task Revogado_CookieRejeitado()
	{
		using var host = await StartAsync(new SessionVersionValidationTests.FakeResolver { Status = new(null, true) });

		(await SignInAndReadProfileAsync(host)).Should().Be("anonimo");
	}

	[Fact]
	public async Task ResolverFalha_CookieRejeitado()
	{
		using var host = await StartAsync(new SessionVersionValidationTests.FakeResolver { Throw = true });

		(await SignInAndReadProfileAsync(host)).Should().Be("anonimo");
	}

	[Fact]
	public async Task SemResolverRegistrado_StartupFalha()
	{
		var start = () => StartAsync(resolver: null);

		await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ISessionVersionResolver*");
	}
}
