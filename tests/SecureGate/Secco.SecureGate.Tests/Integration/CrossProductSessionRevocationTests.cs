extern alias logstream;

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.LogStream.Infrastructure;
using Secco.SDK.AspNetCore.Authentication;
using Secco.SDK.ClientCredentials;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Client.Authorization;
using Secco.SecureGate.Client.Catalog;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// A promessa da ADR-0032 entre produtos reais: um access token de usuário aceito pelo LogStream passa a ser
/// recusado depois de revogar a sessão no SecureGate — sem esperar o token expirar.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class CrossProductSessionRevocationTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "revogacao-cross-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string ResolverClientId = "logstream-sessoes";
	private const string ResolverSecret = "logstream-sessoes-secret-32-chars-min!";
	private const string RoleName = "leitor-logs";

	private readonly string _email = $"cross-{Guid.NewGuid():N}@secco.test";
	private Guid _userId;
	private Guid _tenantId;
	private LogStreamHost _logStream = null!;

	private OidcLoginDriver Driver => new(secureGate, ClientId, RedirectUri, IdentitySeed.Password);

	private sealed class LogStreamHost(SelfIssuedAuthSecureGateApiFactory secureGate, Guid tenantId)
		: WebApplicationFactory<logstream::Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			builder.UseEnvironment("Testing");

			builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Secco:Authentication:Audience"] = "secco-logstream",
					["Secco:Authentication:Authority"] = "http://localhost",
					["Secco:Authentication:RequireHttpsMetadata"] = "false",
					["Secco:Authentication:SessionVersionCacheTtlSeconds"] = "1",
					[$"Secco:Tenancy:Tenants:{tenantId}:ConnectionString"] =
						secureGate.GetConnectionStringFor("secco_logstream_sessoes_e2e"),
					[$"Secco:Authorization:Roles:{RoleName}:Permissions:0"] = "log-entries:read",
				}));

			// ConfigureTestServices: roda DEPOIS das registrações do Program — a troca do resolvedor precisa vencer
			builder.ConfigureTestServices(services =>
			{
				services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
					options.BackchannelHttpHandler = secureGate.Server.CreateHandler());

				// Resolvedor REAL do client, apontado para o SecureGate de teste, com client credentials próprio
				var credentials = new SecureGateClientCredentialsOptions
				{
					BaseUrl = "http://localhost",
					ClientId = ResolverClientId,
					ClientSecret = ResolverSecret,
				};
				services.RemoveAll<ISessionVersionResolver>();
				services.AddSecureGateSessionVersionResolver(_ => credentials);
				services.AddHttpClient(SecureGateSessionVersionResolver.HttpClientName)
					.ConfigurePrimaryHttpMessageHandler(() => secureGate.Server.CreateHandler());
			});
		}
	}

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");
		await secureGate.CreateClientAsync(ResolverClientId, ResolverSecret, SecureGateScopes.AuthorizationRead);

		_tenantId = await IdentitySeed.TenantAsync(secureGate);
		await IdentitySeed.RoleAsync(secureGate, _tenantId, RoleName, "log-entries:read");
		_userId = await IdentitySeed.UserAsync(secureGate, _tenantId, _email, RoleName);

		_logStream = new LogStreamHost(secureGate, _tenantId);
		await _logStream.Services.MigrateLogStreamTenantDatabasesAsync();
	}

	public async Task DisposeAsync() => await _logStream.DisposeAsync();

	private async Task<HttpStatusCode> ReadLogsAsync(string accessToken)
	{
		using var client = _logStream.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		return (await client.GetAsync("/api/v1/log-entries")).StatusCode;
	}

	[Fact]
	public async Task TokenAceito_DepoisDeRevogar_Recusado()
	{
		var session = await Driver.LoginAsync(_email, "openid offline_access logstream");

		(await ReadLogsAsync(session.AccessToken)).Should().Be(HttpStatusCode.OK);

		using (var scope = secureGate.Services.CreateScope())
		{
			await scope.ServiceProvider.GetRequiredService<ISessionRevoker>()
				.RevokeAllAsync(_userId, SessionRevocationReason.AdminRequest);
		}

		await Task.Delay(TimeSpan.FromSeconds(1.5)); // vence o TTL de 1 s do cache do LogStream

		(await ReadLogsAsync(session.AccessToken)).Should().Be(HttpStatusCode.Unauthorized,
			"o access token ainda não expirou, mas a sessão foi revogada");
	}
}
