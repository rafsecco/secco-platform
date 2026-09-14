using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
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
public partial class SessionRevocationTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
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

	[GeneratedRegex("__RequestVerificationToken.*?value=\"([^\"]+)\"", RegexOptions.Singleline)]
	private static partial Regex AntiforgeryField();

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
		_userId = user.Id;
		_tenantId = tenant.Id;
		_otherTenantId = otherTenant.Id;
	}

	public Task DisposeAsync() => Task.CompletedTask;

	// ─────────────────────────────── controle: sessão ativa renova ───────────────────────────────

	[Fact]
	public async Task Refresh_ComContaAtiva_Renova()
	{
		var session = await LoginAsync(_userEmail, UserScope);

		(await RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	// ─────────────────────────────── a sessão aberta termina ───────────────────────────────

	[Fact]
	public async Task Refresh_AposDesativarUsuario_Recusa()
	{
		var session = await LoginAsync(_userEmail, UserScope);
		var admin = await OperatorClientAsync();

		(await admin.PostAsync(DeactivateUserUrl(_tenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		await AssertRefusedAsync(await RefreshAsync(session.RefreshToken));
	}

	[Fact]
	public async Task Refresh_AposBloqueioPorTentativas_Recusa()
	{
		var session = await LoginAsync(_userEmail, UserScope);

		using (var scope = secureGate.Services.CreateScope())
		{
			var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
			var user = await userManager.FindByIdAsync(_userId.ToString());
			(await userManager.SetLockoutEnabledAsync(user!, true)).Succeeded.Should().BeTrue();
			(await userManager.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddMinutes(5))).Succeeded.Should().BeTrue();
		}

		// O CanSignInAsync do Identity diria "pode": ele não olha bloqueio
		await AssertRefusedAsync(await RefreshAsync(session.RefreshToken));
	}

	[Fact]
	public async Task Refresh_AposDesativarTenant_Recusa()
	{
		var session = await LoginAsync(_userEmail, UserScope);
		var admin = await OperatorClientAsync();

		(await admin.PostAsync($"/api/v1/tenants/{_tenantId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		await AssertRefusedAsync(await RefreshAsync(session.RefreshToken));
	}

	[Fact]
	public async Task AuthorizationCode_UsuarioDesativadoEntreLoginETroca_Recusa()
	{
		using var browser = CreateBrowser();
		var (verifier, code) = await ObtainCodeAsync(browser, _userEmail, UserScope);

		var admin = await OperatorClientAsync();
		(await admin.PostAsync(DeactivateUserUrl(_tenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		await AssertRefusedAsync(await ExchangeCodeAsync(browser, code, verifier));
	}

	[Fact]
	public async Task Login_ComUsuarioDesativado_NaoEmiteCode()
	{
		var admin = await OperatorClientAsync();
		(await admin.PostAsync(DeactivateUserUrl(_tenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		using var browser = CreateBrowser();
		var loginPost = await SubmitLoginAsync(browser, _userEmail, UserScope, CreatePkce().Challenge);

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

		var session = await LoginAsync(_userEmail, UserScope);
		(await RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
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
		var session = await LoginAsync(_userEmail, UserScope);
		using var client = BearerClient(session.AccessToken);

		(await client.PostAsync(DeactivateUserUrl(_tenantId, _operatorId), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await client.PostAsync($"/api/v1/tenants/{_tenantId}/users/{_userId}/activate", null))
			.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task Desativar_UsuarioPelaRotaDeOutroTenant_Retorna404ENaoAltera()
	{
		var session = await LoginAsync(_userEmail, UserScope);
		var admin = await OperatorClientAsync();

		(await admin.PostAsync(DeactivateUserUrl(_otherTenantId, _userId), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

		(await RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK, "a rota errada não pode ter desativado ninguém");
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
		var session = await LoginAsync(_operatorEmail, OperatorScope);
		using var admin = BearerClient(session.AccessToken);

		(await admin.PostAsync(DeactivateUserUrl(SecureGatePlatform.TenantId, _operatorId), null))
			.StatusCode.Should().Be(HttpStatusCode.Conflict);

		(await RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task DesativarTenantDePlataforma_Retorna409EOperadorSegue()
	{
		var session = await LoginAsync(_operatorEmail, OperatorScope);
		using var admin = BearerClient(session.AccessToken);

		(await admin.PostAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/deactivate", null))
			.StatusCode.Should().Be(HttpStatusCode.Conflict);

		// Sem a guarda, os operadores parariam de renovar — e reativar exige token de operador
		(await RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
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
		BearerClient((await LoginAsync(_operatorEmail, OperatorScope)).AccessToken);

	private HttpClient BearerClient(string accessToken)
	{
		var client = secureGate.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		return client;
	}

	private HttpClient CreateBrowser() => secureGate.CreateClient(new WebApplicationFactoryClientOptions
	{
		AllowAutoRedirect = false,
		HandleCookies = true,
	});

	private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
	{
		using var client = secureGate.CreateClient();

		return await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["refresh_token"] = refreshToken,
			["client_id"] = ClientId,
		}));
	}

	private async Task<(string AccessToken, string RefreshToken)> LoginAsync(string email, string scope)
	{
		using var browser = CreateBrowser();
		var (verifier, code) = await ObtainCodeAsync(browser, email, scope);

		var response = await ExchangeCodeAsync(browser, code, verifier);
		var body = await response.Content.ReadAsStringAsync();
		response.StatusCode.Should().Be(HttpStatusCode.OK, body);

		using var json = JsonDocument.Parse(body);

		return (json.RootElement.GetProperty("access_token").GetString()!, json.RootElement.GetProperty("refresh_token").GetString()!);
	}

	private static async Task<(string Verifier, string Code)> ObtainCodeAsync(HttpClient browser, string email, string scope)
	{
		var (verifier, challenge) = CreatePkce();

		var loginPost = await SubmitLoginAsync(browser, email, scope, challenge);
		loginPost.StatusCode.Should().Be(HttpStatusCode.Redirect, "credenciais válidas voltam ao authorize");

		var codeResponse = await browser.GetAsync(loginPost.Headers.Location!.ToString());
		codeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

		return (verifier, QueryHelpers.ParseQuery(codeResponse.Headers.Location!.Query)["code"].ToString());
	}

	private static async Task<HttpResponseMessage> SubmitLoginAsync(HttpClient browser, string email, string scope, string challenge)
	{
		var authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
		{
			["response_type"] = "code",
			["client_id"] = ClientId,
			["redirect_uri"] = RedirectUri,
			["scope"] = scope,
			["code_challenge"] = challenge,
			["code_challenge_method"] = "S256",
			["state"] = Guid.NewGuid().ToString("N"),
			["nonce"] = Guid.NewGuid().ToString("N"),
		});

		var loginUrl = (await browser.GetAsync(authorizeUrl)).Headers.Location!.ToString();
		var loginPage = await browser.GetAsync(loginUrl);
		loginPage.EnsureSuccessStatusCode();
		var antiforgery = AntiforgeryField().Match(await loginPage.Content.ReadAsStringAsync()).Groups[1].Value;

		return await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["Input.Email"] = email,
			["Input.Password"] = Password,
			["__RequestVerificationToken"] = antiforgery,
		}));
	}

	private static Task<HttpResponseMessage> ExchangeCodeAsync(HttpClient browser, string code, string verifier) =>
		browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code",
			["code"] = code,
			["redirect_uri"] = RedirectUri,
			["client_id"] = ClientId,
			["code_verifier"] = verifier,
		}));

	private static (string Verifier, string Challenge) CreatePkce()
	{
		var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));

		return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
	}

	private static string Base64Url(byte[] bytes) =>
		Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
