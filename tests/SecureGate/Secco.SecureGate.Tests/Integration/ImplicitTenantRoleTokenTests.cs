using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Clients;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SecureGate.Infrastructure.OpenIddict;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// O perfil implícito (ADR-0056) entra no token de PESSOA com tenant de login — dona ou membro ativo —, em todos os
/// caminhos de emissão (authorize/code e renovação), e fica de fora do que não é pessoa-com-tenant: operador sem
/// tenant, client de máquina, membro suspenso. Tokens emitidos de verdade (code + PKCE).
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class ImplicitTenantRoleTokenTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string RedirectUri = "https://localhost/callback";
	private const string Scope = "openid offline_access logstream";
	private const string MachineSecret = "implicit-machine-secret-de-32-chars-min!!";
	private const string Implicit = SecureGatePlatform.TenantMemberRole;

	private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];
	private Guid _tenantA;
	private Guid _tenantB;

	private string UnboundClient => $"implicito-{_suffix}";

	private string BoundToB => $"implicito-b-{_suffix}";

	private OidcLoginDriver Driver(string clientId) => new(secureGate, clientId, RedirectUri, IdentitySeed.Password);

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();

		_tenantA = await IdentitySeed.TenantAsync(secureGate);
		_tenantB = await IdentitySeed.TenantAsync(secureGate);
		await IdentitySeed.ImplicitRoleAsync(secureGate, _tenantA, "manuais:read");
		await IdentitySeed.ImplicitRoleAsync(secureGate, _tenantB, "politicas:read");
		await IdentitySeed.RoleAsync(secureGate, _tenantA, "editor", "manuais:write");

		await secureGate.CreatePublicClientAsync(UnboundClient, RedirectUri, "logstream", SecureGateScopes.Admin);
		await secureGate.CreatePublicClientAsync(BoundToB, RedirectUri, "logstream");
		await BindAsync(BoundToB, _tenantB);

		await IdentitySeed.UserAsync(secureGate, _tenantA, OwnerEmail(), "editor");
		await IdentitySeed.UserAsync(secureGate, _tenantA, MemberEmail());
		await IdentitySeed.UserAsync(secureGate, _tenantA, SuspendedEmail());
		await IdentitySeed.PlatformOperatorAsync(secureGate, OperatorEmail());

		await AddMembershipAsync(MemberEmail(), _tenantB, suspended: false);
		await AddMembershipAsync(SuspendedEmail(), _tenantB, suspended: false);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private string OwnerEmail() => $"dono-{_suffix}@secco.test";

	private string MemberEmail() => $"membro-{_suffix}@secco.test";

	private string SuspendedEmail() => $"suspenso-{_suffix}@secco.test";

	private string OperatorEmail() => $"op-{_suffix}@secco.test";

	private async Task BindAsync(string clientId, Guid tenantId)
	{
		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var application = await context.Set<OidcApplication>().SingleAsync(a => a.ClientId == clientId);
		application.TenantId = tenantId;
		application.Origin = ClientOrigin.Api; // ck_oidc_applications_origin_tenant (ADR-0037)
		await context.SaveChangesAsync();
	}

	private async Task AddMembershipAsync(string email, Guid tenantId, bool suspended)
	{
		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email);
		var membership = new TenantMembership(user!.Id, tenantId, DateTimeOffset.UtcNow);

		if (suspended)
		{
			membership.Suspend();
		}

		context.TenantMemberships.Add(membership);
		await context.SaveChangesAsync();
	}

	private async Task SuspendAsync(string email, Guid tenantId)
	{
		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email);
		var membership = await context.TenantMemberships.SingleAsync(m => m.UserId == user!.Id && m.TenantId == tenantId);
		membership.Suspend();
		await context.SaveChangesAsync();
	}

	private static IReadOnlyList<string> Roles(string token) =>
		[.. new JsonWebTokenHandler().ReadJsonWebToken(token).Claims.Where(c => c.Type == "role").Select(c => c.Value)];

	private static async Task<(string Access, string Refresh)> ReadAsync(HttpResponseMessage response)
	{
		var body = await response.Content.ReadAsStringAsync();
		response.StatusCode.Should().Be(HttpStatusCode.OK, body);
		using var json = JsonDocument.Parse(body);

		return (json.RootElement.GetProperty("access_token").GetString()!, json.RootElement.GetProperty("refresh_token").GetString()!);
	}

	[Fact]
	public async Task ContaDona_RecebeOImplicito_JuntoComOsPerfisAtribuidos_ENaRenovacao()
	{
		var driver = Driver(UnboundClient);
		var (access, refresh) = await driver.LoginAsync(OwnerEmail(), Scope);

		Roles(access).Should().BeEquivalentTo([Implicit, "editor"]);

		var renewed = await ReadAsync(await driver.RefreshAsync(refresh));
		Roles(renewed.Access).Should().BeEquivalentTo([Implicit, "editor"], "a renovação sai do mesmo ponto que o login");
	}

	[Fact]
	public async Task MembroAtivo_RecebeOImplicitoDoTenantDoLogin_ENaoOsDoTenantDono()
	{
		var driver = Driver(BoundToB);
		var (access, refresh) = await driver.LoginAsync(MemberEmail(), Scope);

		Roles(access).Should().Equal(Implicit);
		new JsonWebTokenHandler().ReadJsonWebToken(access).GetClaim("tenant_id").Value.Should().Be(_tenantB.ToString());

		(Roles((await ReadAsync(await driver.RefreshAsync(refresh))).Access)).Should().Equal(Implicit);
	}

	[Fact]
	public async Task MembroSuspenso_NaoRecebeTokenDoTenant_NemNaRenovacao()
	{
		var driver = Driver(BoundToB);
		var (_, refresh) = await driver.LoginAsync(SuspendedEmail(), Scope);

		await SuspendAsync(SuspendedEmail(), _tenantB);

		(await driver.RefreshAsync(refresh)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

		using var browser = driver.CreateBrowser();
		var denied = await browser.GetAsync(
			$"/connect/authorize?response_type=code&client_id={BoundToB}&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
			+ $"&scope={Uri.EscapeDataString(Scope)}&code_challenge=x&code_challenge_method=S256&state=s&nonce=n");
		denied.StatusCode.Should().NotBe(HttpStatusCode.OK);
	}

	[Fact]
	public async Task OperadorSemTenant_NaoRecebeOImplicito()
	{
		var (access, _) = await Driver(UnboundClient).LoginAsync(OperatorEmail(), "openid offline_access " + SecureGateScopes.Admin);

		var token = new JsonWebTokenHandler().ReadJsonWebToken(access);
		token.Claims.Should().NotContain(c => c.Type == "tenant_id");
		Roles(access).Should().Equal(SecureGatePlatform.OperatorRole);
	}

	[Fact]
	public async Task ClientDeMaquinaDoTenant_NaoRecebeOImplicito()
	{
		var clientId = $"maquina-{_suffix}";
		await secureGate.CreateProductClientAsync(_tenantA, clientId, MachineSecret, "editor", "logstream");

		var response = await secureGate.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = MachineSecret,
			["scope"] = "logstream",
		}));

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
		Roles(payload.GetProperty("access_token").GetString()!).Should().Equal("editor");
	}
}
