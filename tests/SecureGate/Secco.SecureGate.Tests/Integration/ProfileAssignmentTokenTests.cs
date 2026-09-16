using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Mudança de perfil pela API chega ao token na renovação seguinte — com tokens emitidos de verdade: o
/// admin é um operador que fez login, e o usuário tem uma sessão real de code + PKCE.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class ProfileAssignmentTokenTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "perfis-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string UserScope = "openid offline_access logstream";
	private const string OperatorScope = "openid offline_access securegate:admin";

	private readonly string _operatorEmail = $"op-perfis-{Guid.NewGuid():N}@secco.test";
	private readonly string _userEmail = $"perfis-{Guid.NewGuid():N}@secco.test";
	private Guid _operatorId;
	private Guid _userId;
	private Guid _tenantId;

	private OidcLoginDriver Driver => new(secureGate, ClientId, RedirectUri, IdentitySeed.Password);

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, SecureGateScopes.Admin, "logstream");

		_tenantId = await IdentitySeed.TenantAsync(secureGate);
		await IdentitySeed.RoleAsync(secureGate, _tenantId, "inventario-admin", "inventario:write");
		_userId = await IdentitySeed.UserAsync(secureGate, _tenantId, _userEmail);
		_operatorId = await IdentitySeed.PlatformOperatorAsync(secureGate, _operatorEmail);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<HttpClient> OperatorAsync() =>
		Driver.BearerClient((await Driver.LoginAsync(_operatorEmail, OperatorScope)).AccessToken);

	private static async Task<IReadOnlyList<string>> RolesAfterRefreshAsync(HttpResponseMessage refresh)
	{
		var body = await refresh.Content.ReadAsStringAsync();
		refresh.StatusCode.Should().Be(HttpStatusCode.OK, body);

		using var json = JsonDocument.Parse(body);
		var access = new JsonWebTokenHandler().ReadJsonWebToken(json.RootElement.GetProperty("access_token").GetString());

		return [.. access.Claims.Where(c => c.Type == "role").Select(c => c.Value)];
	}

	[Fact]
	public async Task Atribuir_PerfilApareceNoTokenDaRenovacao()
	{
		var session = await Driver.LoginAsync(_userEmail, UserScope);
		using var admin = await OperatorAsync();

		(await admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{_userId}/roles/inventario-admin", null))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await RolesAfterRefreshAsync(await Driver.RefreshAsync(session.RefreshToken))).Should().Contain("inventario-admin");
	}
}
