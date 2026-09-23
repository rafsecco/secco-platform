using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Operador retirado do perfil de operador perde o poder de administrar na renovação seguinte. Antes
/// da correção, a renovação copiava os scopes do token anterior e o operador rebaixado seguia
/// renovando <c>securegate:admin</c> para sempre (refresh deslizante).
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class OperatorDemotionTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "rebaixamento-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string Password = "Rebaix@Secco1!";
	private const string OperatorScope = "openid offline_access securegate:admin";

	private readonly string _email = $"op-rebaix-{Guid.NewGuid():N}@secco.test";
	private Guid _operatorId;

	private OidcLoginDriver Driver => new(secureGate, ClientId, RedirectUri, Password);

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, SecureGateScopes.Admin);

		using var scope = secureGate.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		var normalizedOperator = SecureGatePlatform.OperatorRole.ToUpperInvariant();
		var operatorRole = await context.Roles.FirstAsync(
			r => r.TenantId == SecureGatePlatform.TenantId && r.NormalizedName == normalizedOperator);

		var user = new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = SecureGatePlatform.TenantId,
			UserName = _email,
			Email = _email,
			EmailConfirmed = true,
		};
		(await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
		context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = operatorRole.Id });
		await context.SaveChangesAsync();

		_operatorId = user.Id;

		// A instalação exige 2FA do operador (entrega D); sem isso o login pararia no cadastro.
		await IdentitySeed.EnableTwoFactorAsync(secureGate, _operatorId);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task Refresh_OperadorNoPerfil_MantemScopeAdminSemTenant()
	{
		var session = await Driver.LoginAsync(_email, OperatorScope);

		var access = await ReadAccessTokenAsync(await Driver.RefreshAsync(session.RefreshToken));

		OidcLoginDriver.Scopes(access).Should().Contain(SecureGateScopes.Admin);
		new JsonWebTokenHandler().ReadJsonWebToken(access).TryGetClaim("tenant_id", out _).Should().BeFalse();
	}

	[Fact]
	public async Task Refresh_AposRetirarDoPerfilDeOperador_PerdeScopeAdminERecebeTenant()
	{
		var session = await Driver.LoginAsync(_email, OperatorScope);
		await RemoveOperatorRoleAsync();

		var access = await ReadAccessTokenAsync(await Driver.RefreshAsync(session.RefreshToken));

		OidcLoginDriver.Scopes(access).Should().NotContain(SecureGateScopes.Admin);
		new JsonWebTokenHandler().ReadJsonWebToken(access).GetClaim("tenant_id").Value
			.Should().Be(SecureGatePlatform.TenantId.ToString());
	}

	[Fact]
	public async Task Refresh_AposRetirarDoPerfil_NovoTokenNaoAdministra()
	{
		var session = await Driver.LoginAsync(_email, OperatorScope);
		await RemoveOperatorRoleAsync();

		var access = await ReadAccessTokenAsync(await Driver.RefreshAsync(session.RefreshToken));
		using var client = Driver.BearerClient(access);

		(await client.GetAsync("/api/v1/tenants")).StatusCode
			.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
	}

	private async Task RemoveOperatorRoleAsync()
	{
		using var scope = secureGate.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		// Direto no banco: esta correção não pode depender do endpoint de remoção, que vem depois
		context.UserRoles.RemoveRange(await context.UserRoles.Where(ur => ur.UserId == _operatorId).ToListAsync());
		await context.SaveChangesAsync();
	}

	private static async Task<string> ReadAccessTokenAsync(HttpResponseMessage response)
	{
		var body = await response.Content.ReadAsStringAsync();
		response.StatusCode.Should().Be(HttpStatusCode.OK, body);

		using var json = JsonDocument.Parse(body);

		return json.RootElement.GetProperty("access_token").GetString()!;
	}
}
