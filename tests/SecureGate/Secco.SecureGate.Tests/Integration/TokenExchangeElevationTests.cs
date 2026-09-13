using System.Net;
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
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Elevation;
using Secco.SecureGate.Domain.Elevation;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Token exchange por elevação (ADR-0031), ponta a ponta. O <c>subject_token</c> é sempre um access
/// token EMITIDO de verdade pelo fluxo authorization code + PKCE — nunca um token montado à mão —,
/// porque é exatamente esse token que o <c>ValidateSubjectToken</c> nativo do OpenIddict valida.
/// </summary>
/// <remarks>
/// Um teste prova que a elevação funciona; todos os outros provam que ela NÃO acontece quando não
/// deve. Cada teste negativo corresponde a uma invariante da ADR-0031 ou da sua emenda.
/// </remarks>
[Collection(ElevationApiCollectionDefinition.Name)]
public partial class TokenExchangeElevationTests(ElevationSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "elevation-webapp";
	private const string ClientWithoutExchange = "elevation-webapp-sem-troca";
	private const string MachineClientId = "elevation-machine";
	private const string MachineSecret = "elevation-machine-secret-32-chars!!";
	private const string RedirectUri = "https://localhost/callback";
	private const string Password = "Elev@cao-Secco1";
	private const string LoginScope = "openid profile email roles offline_access logstream";
	private const string TokenExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";
	private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";
	private const string RefreshTokenType = "urn:ietf:params:oauth:token-type:refresh_token";

	/// <summary>Papel próprio do usuário no tenant dele — não pode aparecer no token elevado.</summary>
	private const string UserOwnRole = "admin";

	private readonly string _email = $"elevacao-{Guid.NewGuid():N}@secco.test";
	private Guid _userId;
	private Guid _tenantId;

	[GeneratedRegex("__RequestVerificationToken.*?value=\"([^\"]+)\"", RegexOptions.Singleline)]
	private static partial Regex AntiforgeryField();

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		secureGate.Auditor.Reset();

		await CreateClientWithTokenExchangeAsync();
		await secureGate.CreatePublicClientAsync(ClientWithoutExchange, RedirectUri, "logstream");
		await secureGate.CreateClientAsync(MachineClientId, MachineSecret, "logstream");

		using var scope = secureGate.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		var tenant = new Tenant("Tenant da elevação", $"t-{Guid.NewGuid():N}");
		context.Tenants.Add(tenant);

		// O usuário tem papel PRÓPRIO, com permissão de escrita, no tenant dele. Se esse papel vazasse
		// para o token elevado, um tenant alvo com papel de mesmo nome daria escrita cross-tenant.
		var role = new Role
		{
			Id = Guid.CreateVersion7(),
			TenantId = tenant.Id,
			Name = UserOwnRole,
			NormalizedName = UserOwnRole.ToUpperInvariant(),
			ConcurrencyStamp = Guid.NewGuid().ToString(),
		};
		context.Roles.Add(role);
		context.RoleClaims.Add(new RoleClaim { RoleId = role.Id, ClaimType = "permission", ClaimValue = "log-entries:write" });
		await context.SaveChangesAsync();

		var user = new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = tenant.Id,
			UserName = _email,
			Email = _email,
			EmailConfirmed = true,
			LockoutEnabled = true,
		};
		(await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();

		context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
		await context.SaveChangesAsync();

		_userId = user.Id;
		_tenantId = tenant.Id;
	}

	public Task DisposeAsync() => Task.CompletedTask;

	// ─────────────────────────────── o caso que deve funcionar ───────────────────────────────

	[Fact]
	public async Task Exchange_ComConcessaoVigente_EmiteTokenElevadoEstreito()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();

		var result = await ExchangeAsync(accessToken);

		result.Status.Should().Be(HttpStatusCode.OK, "usuário com concessão vigente e auditoria aceita eleva");
		result.AccessToken.Should().NotBeNullOrEmpty();

		// Invariante 2: nenhum refresh token
		result.RefreshToken.Should().BeNull("token trocado nunca vem com refresh");

		var jwt = new JsonWebTokenHandler().ReadJsonWebToken(result.AccessToken);

		// A pessoa real — e prova que o principal recebido pelo handler era o do subject_token
		jwt.Subject.Should().Be(_userId.ToString());

		// Sem tenant_id: é daqui que vem o alcance cross-tenant
		jwt.Claims.Should().NotContain(claim => claim.Type == "tenant_id");

		// SÓ o papel elevado. O papel próprio do usuário não pode vazar para o token.
		jwt.Claims.Where(claim => claim.Type == "role").Select(claim => claim.Value)
			.Should().BeEquivalentTo([SecureGatePlatform.ElevatedLogReaderRole]);

		// Invariante 4: marcado como trocado, logo não re-trocável
		jwt.GetClaim(SecureGatePlatform.TokenExchangeClaim).Value.Should().Be(SecureGatePlatform.ElevationCapability);

		// Invariante 3: a configuração pediu 600 minutos; o teto é 60
		(jwt.ValidTo - jwt.IssuedAt).Should().BeCloseTo(
			SecureGatePlatform.ElevatedTokenMaxLifetime, TimeSpan.FromSeconds(5),
			$"a configuração pediu {ElevationSecureGateApiFactory.ConfiguredLifetimeMinutes} minutos e o teto vence");

		// Invariante 7: auditado, no tenant de QUEM ELEVOU
		secureGate.Auditor.Records.Should().ContainSingle()
			.Which.Should().Match<ElevationAuditRecord>(record =>
				record.UserId == _userId && record.TenantId == _tenantId);
	}

	// ────────────────────────────────── autoridade ──────────────────────────────────

	[Fact]
	public async Task Exchange_SemConcessao_Recusa()
	{
		var (accessToken, _) = await LoginAsync();

		var result = await ExchangeAsync(accessToken);

		AssertPolicyRefusal(result);
		secureGate.Auditor.Records.Should().BeEmpty("sem concessão não se chega à auditoria");
	}

	[Fact]
	public async Task Exchange_ComConcessaoExpirada_Recusa()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();
		await ExpireGrantAsync();

		AssertPolicyRefusal(await ExchangeAsync(accessToken));
	}

	[Fact]
	public async Task Exchange_ComConcessaoRevogada_Recusa()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();
		(await ExchangeAsync(accessToken)).Status.Should().Be(HttpStatusCode.OK, "pré-condição: a concessão funcionava");

		using (var scope = secureGate.Services.CreateScope())
		{
			await scope.ServiceProvider.GetRequiredService<IElevationGrantRepository>().RemoveAsync(_userId);
		}

		AssertPolicyRefusal(await ExchangeAsync(accessToken));
	}

	// ─────────────────────────── estado atual do usuário e do tenant ───────────────────────────

	[Fact]
	public async Task Exchange_ComUsuarioBloqueado_Recusa()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();

		using (var scope = secureGate.Services.CreateScope())
		{
			var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
			var user = await userManager.FindByIdAsync(_userId.ToString());
			(await userManager.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddHours(1))).Succeeded.Should().BeTrue();
		}

		// O access token segue criptograficamente válido — quem barra é o estado atual do cadastro
		AssertPolicyRefusal(await ExchangeAsync(accessToken));
	}

	[Fact]
	public async Task Exchange_ComTenantDesativado_Recusa()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();

		using (var scope = secureGate.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			(await context.Tenants.SingleAsync(tenant => tenant.Id == _tenantId)).Deactivate();
			await context.SaveChangesAsync();
		}

		// O catálogo não protege aqui: o token elevado leria log de OUTRO tenant, ativo
		AssertPolicyRefusal(await ExchangeAsync(accessToken));
	}

	// ───────────────────────────────── o token de entrada ─────────────────────────────────

	[Fact]
	public async Task Exchange_ComTokenJaElevado_Recusa()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();

		var elevated = await ExchangeAsync(accessToken);
		elevated.Status.Should().Be(HttpStatusCode.OK, "pré-condição: a primeira troca funciona");

		// Invariante 4: sem esta recusa, o usuário renovaria o TTL do token elevado indefinidamente
		var reExchanged = await ExchangeAsync(elevated.AccessToken!);

		reExchanged.Status.Should().NotBe(HttpStatusCode.OK);
		reExchanged.AccessToken.Should().BeNull();
	}

	[Fact]
	public async Task Exchange_ComTokenDeClientCredentials_Recusa()
	{
		await GrantAsync();

		using var client = secureGate.CreateClient();
		var machine = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = MachineClientId,
			["client_secret"] = MachineSecret,
			["scope"] = "logstream",
		}));
		machine.EnsureSuccessStatusCode();

		using var payload = JsonDocument.Parse(await machine.Content.ReadAsStringAsync());
		var machineToken = payload.RootElement.GetProperty("access_token").GetString()!;

		// Invariante 5: máquina não eleva
		AssertRefused(await ExchangeAsync(machineToken));
	}

	[Fact]
	public async Task Exchange_ComRefreshTokenDeclaradoComoRefresh_Recusa()
	{
		var (_, refreshToken) = await LoginAsync();
		await GrantAsync();

		AssertRefused(await ExchangeAsync(refreshToken, subjectTokenType: RefreshTokenType));
	}

	[Fact]
	public async Task Exchange_ComRefreshTokenDeclaradoComoAccessToken_Recusa()
	{
		var (_, refreshToken) = await LoginAsync();
		await GrantAsync();

		// Mentir o tipo não pode fazer um refresh token passar por access token
		AssertRefused(await ExchangeAsync(refreshToken, subjectTokenType: AccessTokenType));
	}

	[Fact]
	public async Task Exchange_ComSubjectTokenAdulterado_Recusa()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();

		// Troca o último caractere da assinatura: payload intacto, assinatura inválida
		var last = accessToken[^1];
		var tampered = accessToken[..^1] + (last == 'A' ? 'B' : 'A');

		AssertRefused(await ExchangeAsync(tampered));
	}

	[Fact]
	public async Task Exchange_ComSubjectTokenMalformado_Recusa()
	{
		await GrantAsync();

		AssertRefused(await ExchangeAsync("isto-nao-e-um-jwt"));
	}

	// ─────────────────────────────────── escopo ───────────────────────────────────

	[Theory]
	[InlineData("logstream securegate:admin")]
	[InlineData("securegate:admin")]
	[InlineData("openid")]
	public async Task Exchange_ComEscopoForaDoAllowlist_Recusa(string scope)
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();

		// Invariante 1: nunca mais largo, e nunca estreitado em silêncio
		AssertRefused(await ExchangeAsync(accessToken, scope: scope));
	}

	[Fact]
	public async Task Exchange_SemEscopo_Recusa()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();

		AssertPolicyRefusal(await ExchangeAsync(accessToken, scope: null));
	}

	// ─────────────────────────────────── client ───────────────────────────────────

	[Fact]
	public async Task Exchange_ComClientSemPermissaoDeTroca_Recusa()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();

		// Segundo portão: usuário autorizado não basta, o client também precisa de permissão
		AssertRefused(await ExchangeAsync(accessToken, clientId: ClientWithoutExchange));
	}

	// ────────────────────────────────── auditoria ──────────────────────────────────

	[Fact]
	public async Task Exchange_ComAuditoriaRecusada_NaoEmiteToken()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();
		secureGate.Auditor.Accept = false;

		var result = await ExchangeAsync(accessToken);

		// Invariante 7: troca não auditada não acontece
		AssertPolicyRefusal(result);
		secureGate.Auditor.Records.Should().ContainSingle("a tentativa de registro aconteceu e foi recusada");
	}

	[Fact]
	public async Task Exchange_SemAuditoriaConfigurada_Recusa()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();
		secureGate.Auditor.IsConfigured = false;

		AssertPolicyRefusal(await ExchangeAsync(accessToken));
		secureGate.Auditor.Records.Should().BeEmpty("sem identidade de auditoria, nada é consultado nem registrado");
	}

	// ─────────────────────────────── indistinguibilidade ───────────────────────────────

	[Fact]
	public async Task Exchange_RecusasDaPolitica_SaoIndistinguiveis()
	{
		var (accessToken, _) = await LoginAsync();

		var withoutGrant = await ExchangeAsync(accessToken);

		await GrantAsync();
		await ExpireGrantAsync();
		var expiredGrant = await ExchangeAsync(accessToken);

		await GrantAsync();
		secureGate.Auditor.Accept = false;
		var auditRefused = await ExchangeAsync(accessToken);

		// Invariante 6: a resposta não pode dizer se o usuário tem concessão, se ela expirou ou se a
		// auditoria falhou — senão a própria recusa vira oráculo
		expiredGrant.Should().BeEquivalentTo(withoutGrant, options => options.Excluding(result => result.RawBody));
		auditRefused.Should().BeEquivalentTo(withoutGrant, options => options.Excluding(result => result.RawBody));
	}

	// ─────────────────────────────────── apoio ───────────────────────────────────

	/// <summary>Recusa da POLÍTICA de elevação: código e descrição fixos (invariante 6).</summary>
	private static void AssertPolicyRefusal(ExchangeResult result)
	{
		AssertRefused(result);
		result.Error.Should().Be(Errors.InvalidGrant);
		result.ErrorDescription.Should().Be("A troca de token não foi autorizada.");
	}

	/// <summary>
	/// Recusa de qualquer camada — da política ou do próprio OpenIddict, que responde antes dela para
	/// client e token de entrada. O que importa aqui é: nenhum token sai.
	/// </summary>
	private static void AssertRefused(ExchangeResult result)
	{
		result.Status.Should().NotBe(HttpStatusCode.OK, $"a troca devia ser recusada, mas respondeu: {result.RawBody}");
		result.AccessToken.Should().BeNull("recusa nunca carrega token");
	}

	private async Task<ExchangeResult> ExchangeAsync(
		string subjectToken,
		string? scope = "logstream",
		string subjectTokenType = AccessTokenType,
		string clientId = ClientId)
	{
		var form = new Dictionary<string, string>
		{
			["grant_type"] = TokenExchangeGrant,
			["client_id"] = clientId,
			["subject_token"] = subjectToken,
			["subject_token_type"] = subjectTokenType,
		};

		if (scope is not null)
		{
			form["scope"] = scope;
		}

		using var client = secureGate.CreateClient();
		using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(form));

		var body = await response.Content.ReadAsStringAsync();
		using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
		var root = document.RootElement;

		return new ExchangeResult(
			response.StatusCode,
			Read(root, "access_token"),
			Read(root, "refresh_token"),
			Read(root, "error"),
			Read(root, "error_description"),
			body);
	}

	private static string? Read(JsonElement root, string property) =>
		root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) ? value.GetString() : null;

	private async Task GrantAsync(DateTimeOffset? expiresAt = null)
	{
		using var scope = secureGate.Services.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IElevationGrantRepository>();

		var now = DateTimeOffset.UtcNow;
		var existing = await repository.GetByUserAsync(_userId);

		if (existing is null)
		{
			await repository.UpsertAsync(new ElevationGrant(_userId, _tenantId, "admin-de-teste", expiresAt, now));
		}
		else
		{
			existing.Renew(expiresAt, "admin-de-teste", now);
			await repository.UpsertAsync(existing);
		}
	}

	/// <summary>
	/// Força a concessão para o passado. O construtor recusa expiração no passado — com razão —, então
	/// o teste escreve direto na coluna, como a própria plataforma faz para chaves fixas no seed.
	/// </summary>
	private async Task ExpireGrantAsync()
	{
		using var scope = secureGate.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		var grant = await context.ElevationGrants.SingleAsync(g => g.UserId == _userId);
		context.Entry(grant).Property(nameof(ElevationGrant.ExpiresAt)).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
		await context.SaveChangesAsync();
	}

	private async Task CreateClientWithTokenExchangeAsync()
	{
		using var scope = secureGate.Services.CreateScope();
		var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

		if (await applications.FindByClientIdAsync(ClientId) is not null)
		{
			return;
		}

		await applications.CreateAsync(new OpenIddictApplicationDescriptor
		{
			ClientId = ClientId,
			ClientType = ClientTypes.Public,
			ConsentType = ConsentTypes.Implicit,
			DisplayName = "Client da elevação (login + troca)",
			RedirectUris = { new Uri(RedirectUri) },
			Permissions =
			{
				Permissions.Endpoints.Authorization,
				Permissions.Endpoints.Token,
				Permissions.Endpoints.EndSession,
				Permissions.GrantTypes.AuthorizationCode,
				Permissions.GrantTypes.RefreshToken,
				Permissions.Prefixes.GrantType + TokenExchangeGrant,
				Permissions.ResponseTypes.Code,
				Permissions.Scopes.Email,
				Permissions.Scopes.Profile,
				Permissions.Scopes.Roles,
				Permissions.Prefixes.Scope + "logstream",
			},
		});
	}

	/// <summary>Login real: authorization code + PKCE, devolvendo o access token e o refresh token.</summary>
	private async Task<(string AccessToken, string RefreshToken)> LoginAsync()
	{
		using var browser = secureGate.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			HandleCookies = true,
		});

		var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
		var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
		var state = Guid.NewGuid().ToString("N");

		var authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
		{
			["response_type"] = "code",
			["client_id"] = ClientId,
			["redirect_uri"] = RedirectUri,
			["scope"] = LoginScope,
			["code_challenge"] = challenge,
			["code_challenge_method"] = "S256",
			["state"] = state,
			["nonce"] = Guid.NewGuid().ToString("N"),
		});

		var challengeResponse = await browser.GetAsync(authorizeUrl);
		var loginUrl = challengeResponse.Headers.Location!.ToString();

		var loginPage = await browser.GetAsync(loginUrl);
		loginPage.EnsureSuccessStatusCode();
		var antiforgery = AntiforgeryField().Match(await loginPage.Content.ReadAsStringAsync()).Groups[1].Value;

		var loginPost = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["Input.Email"] = _email,
			["Input.Password"] = Password,
			["__RequestVerificationToken"] = antiforgery,
		}));

		var codeResponse = await browser.GetAsync(loginPost.Headers.Location!.ToString());
		var code = QueryHelpers.ParseQuery(codeResponse.Headers.Location!.Query)["code"].ToString();

		var tokenResponse = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code",
			["client_id"] = ClientId,
			["code"] = code,
			["redirect_uri"] = RedirectUri,
			["code_verifier"] = verifier,
		}));
		tokenResponse.EnsureSuccessStatusCode();

		using var payload = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());

		return (
			payload.RootElement.GetProperty("access_token").GetString()!,
			payload.RootElement.GetProperty("refresh_token").GetString()!);
	}

	private static string Base64Url(byte[] bytes) =>
		Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	/// <summary>Resposta do <c>/connect/token</c> reduzida ao que os testes comparam.</summary>
	private sealed record ExchangeResult(
		HttpStatusCode Status,
		string? AccessToken,
		string? RefreshToken,
		string? Error,
		string? ErrorDescription,
		string RawBody);
}
