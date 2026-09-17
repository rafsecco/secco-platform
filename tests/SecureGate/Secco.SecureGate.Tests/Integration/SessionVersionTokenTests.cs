using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Todo token de usuário carrega a versão de sessão; token de máquina não (ADR-0032).</summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class SessionVersionTokenTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "sver-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string MachineClientId = "sver-maquina";
	private const string MachineSecret = "sver-maquina-secret-32-chars-minimo!";
	private const string Scope = "openid offline_access logstream";

	private readonly string _email = $"sver-{Guid.NewGuid():N}@secco.test";
	private Guid _userId;

	private OidcLoginDriver Driver => new(secureGate, ClientId, RedirectUri, IdentitySeed.Password);

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");
		await secureGate.CreateClientAsync(MachineClientId, MachineSecret, "logstream");

		var tenantId = await IdentitySeed.TenantAsync(secureGate);
		_userId = await IdentitySeed.UserAsync(secureGate, tenantId, _email);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<string> CurrentVersionAsync()
	{
		using var scope = secureGate.Services.CreateScope();
		var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(_userId.ToString());

		return SessionVersion.From(user!.SecurityStamp);
	}

	[Fact]
	public async Task Login_AccessTokenEIdTokenLevamAVersaoAtual()
	{
		using var browser = Driver.CreateBrowser();
		var (verifier, code) = await Driver.ObtainCodeAsync(browser, _email, Scope);
		var response = await Driver.ExchangeCodeAsync(browser, code, verifier);
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		var handler = new JsonWebTokenHandler();
		var expected = await CurrentVersionAsync();

		handler.ReadJsonWebToken(json.RootElement.GetProperty("access_token").GetString()).GetClaim("sver").Value.Should().Be(expected);
		handler.ReadJsonWebToken(json.RootElement.GetProperty("id_token").GetString()).GetClaim("sver").Value.Should().Be(expected);
	}

	[Fact]
	public async Task Renovacao_LevaAVersaoAtual()
	{
		var session = await Driver.LoginAsync(_email, Scope);
		var refreshed = await Driver.RefreshAsync(session.RefreshToken);
		using var json = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());

		new JsonWebTokenHandler().ReadJsonWebToken(json.RootElement.GetProperty("access_token").GetString())
			.GetClaim("sver").Value.Should().Be(await CurrentVersionAsync());
	}

	[Fact]
	public async Task AccessToken_ExpiraEm5Minutos()
	{
		using var browser = Driver.CreateBrowser();
		var (verifier, code) = await Driver.ObtainCodeAsync(browser, _email, Scope);
		var response = await Driver.ExchangeCodeAsync(browser, code, verifier);
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

		json.RootElement.GetProperty("expires_in").GetInt32().Should().BeInRange(290, 300);
	}

	[Fact]
	public async Task ClientCredentials_SemVersaoDeSessao()
	{
		using var client = secureGate.CreateClient();
		var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = MachineClientId,
			["client_secret"] = MachineSecret,
			["scope"] = "logstream",
		}));
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

		new JsonWebTokenHandler().ReadJsonWebToken(json.RootElement.GetProperty("access_token").GetString())
			.TryGetClaim("sver", out _).Should().BeFalse();
	}
}
