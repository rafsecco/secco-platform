using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
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
/// ADR-0040: a mesma pessoa é membro de vários tenants e o tenant do login vem da aplicação (client
/// interativo vinculado). O token carrega o tenant do login e só os perfis dele; o vínculo é conferido a
/// cada emissão. Cada teste cria os próprios tenants, clients e contas.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class MultiTenantLoginTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string RedirectUri = "https://localhost/callback";
	private const string Password = "Mult1@Tenants!";
	private const string Scope = "openid profile email roles offline_access logstream";

	private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];
	private readonly string _email = $"pessoa-{Guid.NewGuid():N}@secco.test";
	private readonly string _operatorEmail = $"op-{Guid.NewGuid():N}@secco.test";

	private string IntranetClient => $"intranet-{_suffix}";
	private string ComprasClient => $"compras-{_suffix}";
	private string RhClient => $"rh-{_suffix}";

	private Guid _userId;
	private Guid _tenantA;
	private Guid _tenantB;

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();

		using var scope = secureGate.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		var intranet = new Tenant("Intranet", $"a-{_suffix}");
		var compras = new Tenant("Compras", $"b-{_suffix}");
		var rh = new Tenant("RH", $"c-{_suffix}");
		context.Tenants.AddRange(intranet, compras, rh);
		await context.SaveChangesAsync();
		_tenantA = intranet.Id;
		_tenantB = compras.Id;

		// Uma aplicação por tenant: o vínculo do client decide o tenant do login
		await BindClientAsync(context, IntranetClient, intranet.Id);
		// O Compras pode pedir securegate:admin: o teste do operador prova que nem assim o homônimo escala
		await BindClientAsync(context, ComprasClient, compras.Id, "logstream", SecureGateScopes.Admin);
		await BindClientAsync(context, RhClient, rh.Id);

		var colaborador = AddRole(context, intranet.Id, "colaborador");
		var comprador = AddRole(context, compras.Id, "comprador");

		// A pessoa nasce na Intranet (dona) e é membro do Compras. No RH, nenhum vínculo.
		var user = NewUser(intranet.Id, _email);
		(await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
		_userId = user.Id;

		context.TenantMemberships.Add(new TenantMembership(user.Id, compras.Id, DateTimeOffset.UtcNow));
		context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = colaborador.Id });
		context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = comprador.Id });

		// Operador de instalação também membro do Compras, onde existe um HOMÔNIMO do perfil de operador
		var homonimo = AddRole(context, compras.Id, SecureGatePlatform.OperatorRole);
		var operatorRoleName = SecureGatePlatform.OperatorRole.ToUpperInvariant();
		var operatorRole = await context.Roles.FirstAsync(
			r => r.TenantId == SecureGatePlatform.TenantId
				&& r.NormalizedName == operatorRoleName);
		var operatorUser = NewUser(SecureGatePlatform.TenantId, _operatorEmail);
		(await userManager.CreateAsync(operatorUser, Password)).Succeeded.Should().BeTrue();
		await userManager.ResetAuthenticatorKeyAsync(operatorUser);
		(await userManager.SetTwoFactorEnabledAsync(operatorUser, true)).Succeeded.Should().BeTrue();
		context.TenantMemberships.Add(new TenantMembership(operatorUser.Id, compras.Id, DateTimeOffset.UtcNow));
		context.UserRoles.Add(new UserRole { UserId = operatorUser.Id, RoleId = operatorRole.Id });
		context.UserRoles.Add(new UserRole { UserId = operatorUser.Id, RoleId = homonimo.Id });

		await context.SaveChangesAsync();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task MesmaConta_DuasAplicacoes_UmLoginSo_CadaTokenComSeuTenantESeusPerfis()
	{
		var intranet = Driver(IntranetClient);
		var compras = Driver(ComprasClient);
		using var browser = intranet.CreateBrowser();

		// Login na Intranet (dona): tela de senha, como sempre
		var (verifierA, codeA) = await intranet.ObtainCodeAsync(browser, _email, Scope);
		var tokenA = await ReadTokensAsync(await intranet.ExchangeCodeAsync(browser, codeA, verifierA));

		// Compras no MESMO navegador: nenhuma tela — SSO com o tenant do vínculo
		var (verifierB, codeB) = await AuthorizeSilentlyAsync(browser, ComprasClient);
		var tokenB = await ReadTokensAsync(await compras.ExchangeCodeAsync(browser, codeB, verifierB));

		Claim(tokenA.Access, "sub").Should().Be(Claim(tokenB.Access, "sub"), "é a mesma pessoa");
		Claim(tokenA.Access, "tenant_id").Should().Be(_tenantA.ToString());
		Claim(tokenB.Access, "tenant_id").Should().Be(_tenantB.ToString());

		// Perfil de outro tenant nunca no token: Equal (e não Contain) é a prova
		Roles(tokenA.Access).Should().Equal("colaborador");
		Roles(tokenB.Access).Should().Equal("comprador");

		// A renovação mantém o tenant de cada aplicação
		var refreshB = await ReadTokensAsync(await compras.RefreshAsync(tokenB.Refresh));
		Claim(refreshB.Access, "tenant_id").Should().Be(_tenantB.ToString());
		Roles(refreshB.Access).Should().Equal("comprador");

		var refreshA = await ReadTokensAsync(await intranet.RefreshAsync(tokenA.Refresh));
		Claim(refreshA.Access, "tenant_id").Should().Be(_tenantA.ToString());
		Roles(refreshA.Access).Should().Equal("colaborador");
	}

	[Fact]
	public async Task Operador_NoCompras_ComPerfilHomonimo_RecebeTokenComumDoCompras()
	{
		var compras = Driver(ComprasClient);
		using var browser = compras.CreateBrowser();

		var (verifier, code) = await compras.ObtainCodeAsync(browser, _operatorEmail, Scope + " securegate:admin");
		var token = await ReadTokensAsync(await compras.ExchangeCodeAsync(browser, code, verifier));

		// O homônimo no Compras NÃO vira operador: tenant_id presente, sem securegate:admin, e o único
		// perfil é o do Compras — o perfil de operador do tenant de plataforma não viaja junto
		Claim(token.Access, "tenant_id").Should().Be(_tenantB.ToString());
		OidcLoginDriver.Scopes(token.Access).Should().NotContain(SecureGateScopes.Admin);
		Roles(token.Access).Should().Equal(SecureGatePlatform.OperatorRole);
	}

	[Fact]
	public async Task SemVinculo_AuthorizeDevolveAccessDenied_ESessaoSegueValendoNasOutrasAplicacoes()
	{
		var intranet = Driver(IntranetClient);
		using var browser = intranet.CreateBrowser();
		await intranet.ObtainCodeAsync(browser, _email, Scope);

		var rh = await browser.GetAsync(AuthorizeUrl(RhClient, OidcLoginDriver.CreatePkce().Challenge));

		rh.StatusCode.Should().Be(HttpStatusCode.Redirect);
		rh.Headers.Location!.ToString().Should().StartWith(RedirectUri, "a recusa volta ao redirect_uri da aplicação");
		var query = QueryHelpers.ParseQuery(rh.Headers.Location!.Query);
		query["error"].ToString().Should().Be("access_denied");
		query["error_description"].ToString().Should().Be("A conta não tem acesso a esta aplicação.");

		var (_, codeDeNovo) = await AuthorizeSilentlyAsync(browser, IntranetClient);
		codeDeNovo.Should().NotBeEmpty("negar uma aplicação não encerra a sessão da pessoa nas outras");
	}

	[Theory]
	[InlineData(MembershipEnd.Suspended)]
	[InlineData(MembershipEnd.Removed)]
	public async Task VinculoEncerrado_RenovacaoDaqueleTenantFalha_ADaOutraAplicacaoContinua(MembershipEnd end)
	{
		var intranet = Driver(IntranetClient);
		var compras = Driver(ComprasClient);
		using var browser = intranet.CreateBrowser();

		var (verifierA, codeA) = await intranet.ObtainCodeAsync(browser, _email, Scope);
		var tokenA = await ReadTokensAsync(await intranet.ExchangeCodeAsync(browser, codeA, verifierA));
		var (verifierB, codeB) = await AuthorizeSilentlyAsync(browser, ComprasClient);
		var tokenB = await ReadTokensAsync(await compras.ExchangeCodeAsync(browser, codeB, verifierB));

		using (var scope = secureGate.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

			if (end == MembershipEnd.Suspended)
			{
				var membership = await context.TenantMemberships.SingleAsync(m => m.UserId == _userId && m.TenantId == _tenantB);
				membership.Suspend();
				await context.SaveChangesAsync();
			}
			else
			{
				await context.TenantMemberships
					.Where(m => m.UserId == _userId && m.TenantId == _tenantB)
					.ExecuteDeleteAsync();
			}
		}

		// O vínculo da Intranet (dona) segue valendo; o do Compras acabou: só o Compras para
		var refreshCompras = await compras.RefreshAsync(tokenB.Refresh);
		refreshCompras.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await refreshCompras.Content.ReadAsStringAsync()).Should().Contain("invalid_grant");

		var refreshIntranet = await ReadTokensAsync(await intranet.RefreshAsync(tokenA.Refresh));
		Claim(refreshIntranet.Access, "tenant_id").Should().Be(_tenantA.ToString());
	}

	[Fact]
	public async Task TenantDoLoginDesativado_SemTokenNaquelaAplicacao_AsOutrasSeguem()
	{
		var intranet = Driver(IntranetClient);
		var compras = Driver(ComprasClient);
		using var browser = intranet.CreateBrowser();

		var (verifierA, codeA) = await intranet.ObtainCodeAsync(browser, _email, Scope);
		await intranet.ExchangeCodeAsync(browser, codeA, verifierA);
		var (verifierB, codeB) = await AuthorizeSilentlyAsync(browser, ComprasClient);
		var tokenB = await ReadTokensAsync(await compras.ExchangeCodeAsync(browser, codeB, verifierB));

		await SetTenantActiveAsync(_tenantB, active: false);

		var denied = await browser.GetAsync(AuthorizeUrl(ComprasClient, OidcLoginDriver.CreatePkce().Challenge));
		denied.StatusCode.Should().Be(HttpStatusCode.Redirect);
		QueryHelpers.ParseQuery(denied.Headers.Location!.Query)["error"].ToString().Should().Be("access_denied");

		(await compras.RefreshAsync(tokenB.Refresh)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

		var (_, codeIntranet) = await AuthorizeSilentlyAsync(browser, IntranetClient);
		codeIntranet.Should().NotBeEmpty("o tenant do login inativo só afeta aquela aplicação");
	}

	[Fact]
	public async Task TenantDonoDesativado_SemTokenEmNenhumaAplicacao()
	{
		var intranet = Driver(IntranetClient);
		var compras = Driver(ComprasClient);
		using var browser = intranet.CreateBrowser();

		var (verifierA, codeA) = await intranet.ObtainCodeAsync(browser, _email, Scope);
		var tokenA = await ReadTokensAsync(await intranet.ExchangeCodeAsync(browser, codeA, verifierA));
		var (verifierB, codeB) = await AuthorizeSilentlyAsync(browser, ComprasClient);
		var tokenB = await ReadTokensAsync(await compras.ExchangeCodeAsync(browser, codeB, verifierB));

		await SetTenantActiveAsync(_tenantA, active: false);

		// Renovação: nenhuma das duas aplicações
		(await intranet.RefreshAsync(tokenA.Refresh)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await compras.RefreshAsync(tokenB.Refresh)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

		// Authorize: a conta não recebe código — o caminho de conta bloqueada, que encerra o cookie e
		// desafia o login de novo (o vínculo com o Compras, ativo, não compensa o dono inativo)
		var authorize = await browser.GetAsync(AuthorizeUrl(ComprasClient, OidcLoginDriver.CreatePkce().Challenge));
		authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
		authorize.Headers.Location!.ToString().Should().NotStartWith(RedirectUri);
		authorize.Headers.Location!.ToString().Should().Contain("/login");
	}

	public enum MembershipEnd
	{
		Suspended,
		Removed,
	}

	private OidcLoginDriver Driver(string clientId) => new(secureGate, clientId, RedirectUri, Password);

	private async Task SetTenantActiveAsync(Guid tenantId, bool active)
	{
		using var scope = secureGate.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var tenant = await context.Tenants.SingleAsync(t => t.Id == tenantId);

		if (active)
		{
			tenant.Activate();
		}
		else
		{
			tenant.Deactivate();
		}

		await context.SaveChangesAsync();
	}

	private static async Task<(string Verifier, string Code)> AuthorizeSilentlyAsync(HttpClient browser, string clientId)
	{
		var (verifier, challenge) = OidcLoginDriver.CreatePkce();
		var response = await browser.GetAsync(AuthorizeUrl(clientId, challenge));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		response.Headers.Location!.ToString().Should().StartWith(RedirectUri, "com sessão aberta não há tela de login");

		return (verifier, QueryHelpers.ParseQuery(response.Headers.Location!.Query)["code"].ToString());
	}

	private static string AuthorizeUrl(string clientId, string challenge) =>
		QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
		{
			["response_type"] = "code",
			["client_id"] = clientId,
			["redirect_uri"] = RedirectUri,
			["scope"] = Scope,
			["code_challenge"] = challenge,
			["code_challenge_method"] = "S256",
			["state"] = Guid.NewGuid().ToString("N"),
			["nonce"] = Guid.NewGuid().ToString("N"),
		});

	private static async Task<(string Access, string Refresh)> ReadTokensAsync(HttpResponseMessage response)
	{
		var body = await response.Content.ReadAsStringAsync();
		response.StatusCode.Should().Be(HttpStatusCode.OK, body);
		using var json = JsonDocument.Parse(body);

		return (json.RootElement.GetProperty("access_token").GetString()!,
			json.RootElement.GetProperty("refresh_token").GetString()!);
	}

	private static string? Claim(string token, string type) =>
		new JsonWebTokenHandler().ReadJsonWebToken(token).Claims.FirstOrDefault(c => c.Type == type)?.Value;

	private static IReadOnlyList<string> Roles(string token) =>
		[.. new JsonWebTokenHandler().ReadJsonWebToken(token).Claims.Where(c => c.Type == "role").Select(c => c.Value)];

	private static User NewUser(Guid tenantId, string email) => new()
	{
		Id = Guid.CreateVersion7(),
		TenantId = tenantId,
		UserName = email,
		Email = email,
		EmailConfirmed = true,
	};

	private static Role AddRole(SecureGateDbContext context, Guid tenantId, string name)
	{
		var role = new Role
		{
			Id = Guid.CreateVersion7(),
			TenantId = tenantId,
			Name = name,
			NormalizedName = name.ToUpperInvariant(),
			ConcurrencyStamp = Guid.NewGuid().ToString(),
		};
		context.Roles.Add(role);

		return role;
	}

	private async Task BindClientAsync(SecureGateDbContext context, string clientId, Guid tenantId, params string[] scopes)
	{
		await secureGate.CreatePublicClientAsync(clientId, RedirectUri, scopes.Length == 0 ? ["logstream"] : scopes);
		var application = await context.Set<OidcApplication>().SingleAsync(a => a.ClientId == clientId);
		application.TenantId = tenantId;
		application.Origin = ClientOrigin.Api; // ck_oidc_applications_origin_tenant (ADR-0037)
		await context.SaveChangesAsync();
	}
}
