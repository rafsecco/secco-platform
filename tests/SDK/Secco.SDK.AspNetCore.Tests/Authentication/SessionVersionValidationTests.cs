using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Secco.SDK.AspNetCore.Authentication;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Authentication;

/// <summary>O produto recusa token de sessão revogada (ADR-0032), com cache e fail-closed.</summary>
public class SessionVersionValidationTests : IAsyncLifetime
{
	private const string SigningKey = "chave-de-testes-com-32-caracteres!!";
	private const string Issuer = "secco-tests";
	private const string Audience = "secco-tests";

	private readonly FakeResolver _resolver = new();
	private IHost _host = null!;

	public sealed class FakeResolver : ISessionVersionResolver
	{
		public bool IsEnabled { get; set; } = true;

		public SessionVersionStatus Status { get; set; } = new("v1", false);

		public bool Throw { get; set; }

		public int Calls { get; private set; }

		public ValueTask<SessionVersionStatus> ResolveAsync(string subject, CancellationToken cancellationToken = default)
		{
			Calls++;

			return Throw ? throw new HttpRequestException("SecureGate fora") : ValueTask.FromResult(Status);
		}
	}

	public async Task InitializeAsync()
	{
		_host = await new HostBuilder()
			.ConfigureWebHost(web =>
			{
				web.UseTestServer();
				web.UseEnvironment(Environments.Development);
				web.ConfigureAppConfiguration((_, configuration) =>
					configuration.AddInMemoryCollection(new Dictionary<string, string?>
					{
						["Secco:Authentication:Audience"] = Audience,
						["Secco:Authentication:Issuer"] = Issuer,
						["Secco:Authentication:DevelopmentSigningKey"] = SigningKey,
						["Secco:Authentication:SessionVersionCacheTtlSeconds"] = "60",
					}));
				web.ConfigureServices(services =>
				{
					services.AddRouting();
					services.AddSeccoPlatform();
					services.AddSingleton<ISessionVersionResolver>(_resolver);
				});
				web.Configure(app =>
				{
					app.UseRouting();
					app.UseSeccoPlatform();
					app.UseEndpoints(endpoints => endpoints.MapGet("/protegido", () => "ok"));
				});
			})
			.StartAsync();
	}

	public async Task DisposeAsync()
	{
		await _host.StopAsync();
		_host.Dispose();
	}

	private static string Token(string subject, string? sessionVersion)
	{
		var claims = new Dictionary<string, object> { [SeccoClaims.Subject] = subject };

		if (sessionVersion is not null)
		{
			claims[SeccoClaims.SessionVersion] = sessionVersion;
		}

		return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
		{
			Issuer = Issuer,
			Audience = Audience,
			Claims = claims,
			Expires = DateTime.UtcNow.AddMinutes(5),
			SigningCredentials = new SigningCredentials(
				new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256),
		});
	}

	private Task<HttpResponseMessage> GetAsync(string token)
	{
		var client = _host.GetTestClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

		return client.GetAsync("/protegido");
	}

	private static string Subject() => Guid.NewGuid().ToString();

	[Fact]
	public async Task VersaoIgual_Passa() =>
		(await GetAsync(Token(Subject(), "v1"))).StatusCode.Should().Be(HttpStatusCode.OK);

	[Fact]
	public async Task VersaoDiferente_401()
	{
		_resolver.Status = new SessionVersionStatus("v2", false);

		(await GetAsync(Token(Subject(), "v1"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task Revogado_401()
	{
		_resolver.Status = new SessionVersionStatus(null, true);

		(await GetAsync(Token(Subject(), "v1"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task SemSver_PassaSemConsultar()
	{
		(await GetAsync(Token(Subject(), null))).StatusCode.Should().Be(HttpStatusCode.OK);

		_resolver.Calls.Should().Be(0);
	}

	[Fact]
	public async Task ResolverDesabilitado_PassaSemConsultar()
	{
		_resolver.IsEnabled = false;
		_resolver.Status = new SessionVersionStatus(null, true);

		(await GetAsync(Token(Subject(), "v1"))).StatusCode.Should().Be(HttpStatusCode.OK);
		_resolver.Calls.Should().Be(0);
	}

	[Fact]
	public async Task ResolverFalha_FailClosed401()
	{
		_resolver.Throw = true;

		(await GetAsync(Token(Subject(), "v1"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task DuasRequisicoesNoTtl_UmaConsulta()
	{
		var subject = Subject();

		(await GetAsync(Token(subject, "v1"))).StatusCode.Should().Be(HttpStatusCode.OK);
		(await GetAsync(Token(subject, "v1"))).StatusCode.Should().Be(HttpStatusCode.OK);

		_resolver.Calls.Should().Be(1);
	}
}
