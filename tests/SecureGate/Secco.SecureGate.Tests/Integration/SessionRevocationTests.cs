using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SecureGate.Infrastructure.OpenIddict;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Encerramento de sessão: desativar usuário, bloqueio e tenant desativado têm de valer para uma
/// sessão JÁ ABERTA — na renovação de token e na troca do authorization code —, e não só para o
/// próximo login. Com a expiração deslizante do refresh token, a renovação é o único ponto em que
/// uma sessão em uso termina.
/// </summary>
/// <remarks>
/// Tudo dirigido por HTTP e com tokens emitidos de verdade: o administrador é um operador de
/// instalação que fez login, e as sessões revogadas são sessões reais do fluxo code + PKCE.
/// </remarks>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class SessionRevocationTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "revogacao-e2e";
	private const string RedirectUri = "https://localhost/callback";
	private const string Password = "Revog@Secco1!";
	private const string UserScope = "openid offline_access logstream";
	private const string OperatorScope = "openid offline_access securegate:admin";

	private readonly string _operatorEmail = $"op-revog-{Guid.NewGuid():N}@secco.test";
	private readonly string _userEmail = $"revog-{Guid.NewGuid():N}@secco.test";
	private Guid _operatorId;
	private Guid _userId;
	private Guid _tenantId;
	private Guid _otherTenantId;

	private OidcLoginDriver Driver => new(secureGate, ClientId, RedirectUri, Password);

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, "securegate:admin", "logstream");

		using var scope = secureGate.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		var normalizedOperator = SecureGatePlatform.OperatorRole.ToUpperInvariant();
		var operatorRole = await context.Roles.FirstAsync(
			r => r.TenantId == SecureGatePlatform.TenantId && r.NormalizedName == normalizedOperator);

		var operatorUser = NewUser(SecureGatePlatform.TenantId, _operatorEmail);
		(await userManager.CreateAsync(operatorUser, Password)).Succeeded.Should().BeTrue();
		context.UserRoles.Add(new UserRole { UserId = operatorUser.Id, RoleId = operatorRole.Id });

		var tenant = new Tenant("Tenant da revogação", $"t-{Guid.NewGuid():N}");
		var otherTenant = new Tenant("Outro tenant da revogação", $"t-{Guid.NewGuid():N}");
		context.Tenants.AddRange(tenant, otherTenant);
		await context.SaveChangesAsync();

		var user = NewUser(tenant.Id, _userEmail);
		(await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();

		// Lockout DESLIGADO de propósito — o pior caso. Com ele desligado, o Identity ignora a data de
		// bloqueio: uma desativação que só gravasse a data não teria efeito nenhum.
		(await userManager.SetLockoutEnabledAsync(user, false)).Succeeded.Should().BeTrue();

		_operatorId = operatorUser.Id;

		// A instalação exige 2FA do operador (entrega D); sem isso o login pararia no cadastro.
		await IdentitySeed.EnableTwoFactorAsync(secureGate, _operatorId);
		_userId = user.Id;
		_tenantId = tenant.Id;
		_otherTenantId = otherTenant.Id;
	}

	public Task DisposeAsync() => Task.CompletedTask;

	// ─────────────────────────────── controle: sessão ativa renova ───────────────────────────────

	[Fact]
	public async Task Refresh_ComContaAtiva_Renova()
	{
		var session = await Driver.LoginAsync(_userEmail, UserScope);

		(await Driver.RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	// ─────────────────────────────── a sessão aberta termina ───────────────────────────────

	[Fact]
	public async Task Refresh_AposDesativarUsuario_Recusa()
	{
		var session = await Driver.LoginAsync(_userEmail, UserScope);
		var admin = await OperatorClientAsync();

		(await admin.PostAsync(DeactivateUserUrl(_tenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		await AssertRefusedAsync(await Driver.RefreshAsync(session.RefreshToken));
	}

	[Fact]
	public async Task Refresh_AposBloqueioPorTentativas_Recusa()
	{
		var session = await Driver.LoginAsync(_userEmail, UserScope);

		using (var scope = secureGate.Services.CreateScope())
		{
			var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
			var user = await userManager.FindByIdAsync(_userId.ToString());
			(await userManager.SetLockoutEnabledAsync(user!, true)).Succeeded.Should().BeTrue();
			(await userManager.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddMinutes(5))).Succeeded.Should().BeTrue();
		}

		// O CanSignInAsync do Identity diria "pode": ele não olha bloqueio
		await AssertRefusedAsync(await Driver.RefreshAsync(session.RefreshToken));
	}

	[Fact]
	public async Task Refresh_AposDesativarTenant_Recusa()
	{
		var session = await Driver.LoginAsync(_userEmail, UserScope);
		var admin = await OperatorClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{_tenantId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		await AssertRefusedAsync(await Driver.RefreshAsync(session.RefreshToken));
	}

	[Fact]
	public async Task AuthorizationCode_UsuarioDesativadoEntreLoginETroca_Recusa()
	{
		using var browser = Driver.CreateBrowser();
		var (verifier, code) = await Driver.ObtainCodeAsync(browser, _userEmail, UserScope);

		var admin = await OperatorClientAsync();
		(await admin.PostAsync(DeactivateUserUrl(_tenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		await AssertRefusedAsync(await Driver.ExchangeCodeAsync(browser, code, verifier));
	}

	[Fact]
	public async Task Login_ComUsuarioDesativado_NaoEmiteCode()
	{
		var admin = await OperatorClientAsync();
		(await admin.PostAsync(DeactivateUserUrl(_tenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		using var browser = Driver.CreateBrowser();
		var loginPost = await Driver.SubmitLoginAsync(browser, _userEmail, UserScope, OidcLoginDriver.CreatePkce().Challenge);

		// Sem redirect de volta ao authorize: a tela é re-renderizada com o erro
		loginPost.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Reativar_DevolveOAcesso()
	{
		var admin = await OperatorClientAsync();
		(await admin.PostAsync(DeactivateUserUrl(_tenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{_userId}/activate", null))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var session = await Driver.LoginAsync(_userEmail, UserScope);
		(await Driver.RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	// ─────────────────────────────── perímetro dos endpoints ───────────────────────────────

	[Fact]
	public async Task Desativar_SemToken_Retorna401()
	{
		using var anonymous = secureGate.CreateClient();

		(await anonymous.PostAsync(DeactivateUserUrl(_tenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task Desativar_ComTokenDeUsuarioParaOutroProduto_Retorna401()
	{
		// Token legítimo, emitido de verdade — mas para o LogStream: a audience não é a do SecureGate.
		// O 403 por falta de securegate:admin está em UserManagementTests, onde dá para cunhar o token.
		var session = await Driver.LoginAsync(_userEmail, UserScope);
		using var client = Driver.BearerClient(session.AccessToken);

		(await client.PostAsync(DeactivateUserUrl(_tenantId, _operatorId), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await client.PostAsync($"/api/v1/tenants/{_tenantId}/users/{_userId}/activate", null))
			.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task Desativar_UsuarioPelaRotaDeOutroTenant_Retorna404ENaoAltera()
	{
		var session = await Driver.LoginAsync(_userEmail, UserScope);
		var admin = await OperatorClientAsync();

		(await admin.PostAsync(DeactivateUserUrl(_otherTenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

		(await Driver.RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "a rota errada não pode ter desativado ninguém");
	}

	[Fact]
	public async Task Desativar_UsuarioInexistente_Retorna404()
	{
		var admin = await OperatorClientAsync();

		(await admin.PostAsync(DeactivateUserUrl(_tenantId, Guid.CreateVersion7()), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	// ─────────────────────────────── guardas contra auto-bloqueio ───────────────────────────────

	[Fact]
	public async Task Desativar_APropriaConta_Retorna409ESessaoSegue()
	{
		var session = await Driver.LoginAsync(_operatorEmail, OperatorScope);
		using var admin = Driver.BearerClient(session.AccessToken);

		(await admin.PostAsync(DeactivateUserUrl(SecureGatePlatform.TenantId, _operatorId), null))
			.StatusCode.Should().Be(HttpStatusCode.Conflict);

		(await Driver.RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task DesativarTenantDePlataforma_Retorna409EOperadorSegue()
	{
		var session = await Driver.LoginAsync(_operatorEmail, OperatorScope);
		using var admin = Driver.BearerClient(session.AccessToken);

		(await admin.PostAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/deactivate", null))
			.StatusCode.Should().Be(HttpStatusCode.Conflict);

		// Sem a guarda, os operadores parariam de renovar — e reativar exige token de operador
		(await Driver.RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	// ─────────────────────────────── infraestrutura do teste ───────────────────────────────

	private static User NewUser(Guid tenantId, string email) => new()
	{
		Id = Guid.CreateVersion7(),
		TenantId = tenantId,
		UserName = email,
		Email = email,
		EmailConfirmed = true,
	};

	private static string DeactivateUserUrl(Guid tenantId, Guid userId) =>
		$"/api/v1/tenants/{tenantId}/users/{userId}/deactivate";

	private static async Task AssertRefusedAsync(HttpResponseMessage response)
	{
		var body = await response.Content.ReadAsStringAsync();

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
		using var json = JsonDocument.Parse(body);
		json.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
		json.RootElement.TryGetProperty("access_token", out _).Should().BeFalse();
	}

	private async Task<HttpClient> OperatorClientAsync() =>
		Driver.BearerClient((await Driver.LoginAsync(_operatorEmail, OperatorScope)).AccessToken);

	[Fact]
	public async Task DesativarEReativar_RefreshAnteriorContinuaRecusado()
	{
		var session = await Driver.LoginAsync(_userEmail, UserScope);
		var admin = await OperatorClientAsync();

		(await admin.PostAsync(DeactivateUserUrl(_tenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{_userId}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		// Reativada, a conta volta a logar — mas o refresh emitido antes foi revogado, não só bloqueado
		await AssertRefusedAsync(await Driver.RefreshAsync(session.RefreshToken));
	}

	[Fact]
	public async Task EncerrarSessoes_RefreshRecusadoNaHora()
	{
		var session = await Driver.LoginAsync(_userEmail, UserScope);
		var admin = await OperatorClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{_userId}/sessions/revoke", null))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		await AssertRefusedAsync(await Driver.RefreshAsync(session.RefreshToken));
	}

	[Fact]
	public async Task Cookie_DepoisDeEncerrarSessoes_NaoEmiteNovoCode()
	{
		using var browser = Driver.CreateBrowser();
		await Driver.ObtainCodeAsync(browser, _userEmail, UserScope);

		// Controle: com o cookie válido, o authorize emite code direto para o redirect_uri
		var before = await Driver.AuthorizeAsync(browser, UserScope);
		before.Headers.Location!.ToString().Should().StartWith(RedirectUri);

		var admin = await OperatorClientAsync();
		(await admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{_userId}/sessions/revoke", null))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var after = await Driver.AuthorizeAsync(browser, UserScope);

		after.StatusCode.Should().Be(HttpStatusCode.Redirect);
		after.Headers.Location!.ToString().Should().Contain("/login", "cookie de sessão revogada não pode gerar token novo");
	}

	[Fact]
	public async Task EncerrarSessoes_MarcaOsTokensDoUsuarioComoRevogados()
	{
		await Driver.LoginAsync(_userEmail, UserScope);
		var admin = await OperatorClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{_userId}/sessions/revoke", null))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		// Não basta a autorização cair: os registros de token do usuário também saem de "valid"
		using var scope = secureGate.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var subject = _userId.ToString();
		var statuses = await context.Set<OidcToken>()
			.AsNoTracking()
			.Where(token => token.Subject == subject)
			.Select(token => token.Status)
			.ToListAsync();

		statuses.Should().NotBeEmpty("o login gravou tokens para este usuário");
		statuses.Should().OnlyContain(status => status != "valid");
	}
}
