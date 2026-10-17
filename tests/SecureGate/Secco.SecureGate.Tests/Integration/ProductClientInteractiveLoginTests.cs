using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Ponta a ponta do client interativo criado pela API (ADR-0040): a gestão usa um token emitido de
/// verdade, o login usa o client confidencial com PKCE e o token sai com o tenant do vínculo.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class ProductClientInteractiveLoginTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string AdminClientId = "interactive-client-admin";
	private const string AdminClientSecret = "interactive-client-admin-secret-32-chars!!";
	private const string RedirectUri = "https://app.testes.local/callback";
	private const string Password = "Inter@tivo-Secco1!";
	private const string Scope = "openid profile email roles offline_access logstream";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];
	private readonly string _email = $"pessoa-{Guid.NewGuid():N}@secco.test";

	private HttpClient _admin = null!;
	private Guid _ownerTenantId;
	private Guid _linkedTenantId;
	private Guid _userId;

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreateClientAsync(AdminClientId, AdminClientSecret, SecureGateScopes.Admin);

		var token = await secureGate.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(
			new Dictionary<string, string>
			{
				["grant_type"] = "client_credentials",
				["client_id"] = AdminClientId,
				["client_secret"] = AdminClientSecret,
				["scope"] = SecureGateScopes.Admin,
			}));
		token.StatusCode.Should().Be(HttpStatusCode.OK);
		_admin = secureGate.CreateClient();
		_admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
			"Bearer", (await token.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString());

		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		// A pessoa nasce no tenant A (dona), é membro do tenant B (do client) e tem um perfil em cada um
		var owner = new Tenant("Dona", $"dona-{_suffix}");
		var linked = new Tenant("Vinculado", $"vinc-{_suffix}");
		context.Tenants.AddRange(owner, linked);
		await context.SaveChangesAsync();
		_ownerTenantId = owner.Id;
		_linkedTenantId = linked.Id;

		var user = new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = _ownerTenantId,
			UserName = _email,
			Email = _email,
			EmailConfirmed = true,
		};
		(await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
		_userId = user.Id;

		context.TenantMemberships.Add(new TenantMembership(_userId, _linkedTenantId, DateTimeOffset.UtcNow));
		await context.SaveChangesAsync();

		await AssignRoleAsync(_ownerTenantId, "perfil-da-dona");
		await AssignRoleAsync(_linkedTenantId, "perfil-do-vinculo");
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task AssignRoleAsync(Guid tenantId, string name)
	{
		await IdentitySeed.RoleAsync(secureGate, tenantId, name, "log-entries:read");

		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var normalized = name.ToUpperInvariant();
		var role = await context.Roles.SingleAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized);
		context.UserRoles.Add(new UserRole { UserId = _userId, RoleId = role.Id });
		await context.SaveChangesAsync();
	}

	private async Task<(string ClientId, string Secret)> CreateInteractiveClientAsync()
	{
		var response = await _admin.PostAsJsonAsync($"/api/v1/tenants/{_linkedTenantId}/clients", new
		{
			name = $"Interativo {Guid.NewGuid():N}",
			scopes = new[] { "logstream" },
			roles = Array.Empty<string>(),
			type = "authorization_code",
			redirectUris = new[] { RedirectUri },
		});
		response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
		var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

		return (created.GetProperty("clientId").GetString()!, created.GetProperty("clientSecret").GetString()!);
	}

	[Fact]
	public async Task Login_ComClientCriadoPelaApi_EmiteOTenantDoVinculoEPerfisSoDele()
	{
		var (clientId, secret) = await CreateInteractiveClientAsync();
		var driver = new OidcLoginDriver(secureGate, clientId, RedirectUri, Password, secret);
		using var browser = driver.CreateBrowser();

		var (verifier, code) = await driver.ObtainCodeAsync(browser, _email, Scope);

		// Confidencial: sem o secret a troca do code é recusada
		var anonymous = new OidcLoginDriver(secureGate, clientId, RedirectUri, Password);
		using var anonymousBrowser = anonymous.CreateBrowser();
		(await anonymous.ExchangeCodeAsync(anonymousBrowser, code, verifier))
			.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);

		// O code é de uso único e a recusa pode tê-lo consumido: outro navegador faz um login novo
		using var secondBrowser = driver.CreateBrowser();
		var (verifier2, code2) = await driver.ObtainCodeAsync(secondBrowser, _email, Scope);
		var response = await driver.ExchangeCodeAsync(secondBrowser, code2, verifier2);
		var body = await response.Content.ReadAsStringAsync();
		response.StatusCode.Should().Be(HttpStatusCode.OK, body);

		using var tokens = JsonDocument.Parse(body);
		var access = new JsonWebTokenHandler().ReadJsonWebToken(tokens.RootElement.GetProperty("access_token").GetString());

		access.GetClaim("tenant_id").Value.Should().Be(_linkedTenantId.ToString(), "o tenant do login é o do client, não o da conta");
		access.Claims.Where(c => c.Type == "role").Select(c => c.Value).Should().Equal(["perfil-do-vinculo"]);

		// A renovação mantém o tenant do client (e exige o secret, como a troca do code)
		var refresh = await driver.RefreshAsync(tokens.RootElement.GetProperty("refresh_token").GetString()!);
		var refreshBody = await refresh.Content.ReadAsStringAsync();
		refresh.StatusCode.Should().Be(HttpStatusCode.OK, refreshBody);
		using var renewed = JsonDocument.Parse(refreshBody);
		new JsonWebTokenHandler().ReadJsonWebToken(renewed.RootElement.GetProperty("access_token").GetString())
			.GetClaim("tenant_id").Value.Should().Be(_linkedTenantId.ToString());
	}

	[Fact]
	public async Task Authorize_ClientCriadoPelaApi_SemPkce_Recusado()
	{
		var (clientId, secret) = await CreateInteractiveClientAsync();
		var driver = new OidcLoginDriver(secureGate, clientId, RedirectUri, Password, secret);
		using var browser = driver.CreateBrowser();

		var url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
		{
			["response_type"] = "code",
			["client_id"] = clientId,
			["redirect_uri"] = RedirectUri,
			["scope"] = Scope,
			["state"] = "s",
			["nonce"] = "n",
		});

		var response = await browser.GetAsync(url);

		// O OpenIddict recusa antes de qualquer tela de login: nunca um code
		var location = response.Headers.Location?.ToString() ?? string.Empty;
		location.Should().NotContain("code=");
		location.Should().NotContain("/login", "sem PKCE o fluxo nem chega à tela de login");
		response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.BadRequest);
	}
}
