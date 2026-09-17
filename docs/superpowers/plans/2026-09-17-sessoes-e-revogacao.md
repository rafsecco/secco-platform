# Sessões e revogação efetiva — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Revogar a sessão de um usuário passa a valer em todos os produtos e nas aplicações de cookie em até um TTL de cache, inclusive contra access token já emitido e cookie de login roubado (ADR-0032).

**Architecture:** O SecureGate emite a claim `sver` (hash do `SecurityStamp`) em todo token de usuário, expõe a versão atual por endpoint e revoga numa operação única (stamp → OpenIddict). O SDK confere a `sver` com cache fail-closed no JwtBearer dos produtos e no cookie das aplicações clientes; o `Secco.SecureGate.Client` fornece o resolvedor remoto. O AdminPortal passa a guardar tokens num cofre no servidor e a renová-los.

**Tech Stack:** .NET 10, ASP.NET Core, ASP.NET Identity + OpenIddict 7.5, NSwag, Blazor Server, xUnit + FluentAssertions 7 + NSubstitute + Testcontainers.

**Spec:** [`docs/superpowers/specs/2026-09-17-sessoes-e-revogacao-design.md`](../specs/2026-09-17-sessoes-e-revogacao-design.md) · **ADR:** ADR-0032 (Aceita)

## Global Constraints

- Claim curta `sver` = `SeccoClaims.SessionVersion`; valor `Base64Url(SHA-256(SecurityStamp))` truncado em 16 caracteres; nunca o stamp cru.
- `sver` em access token **e** id_token de login/renovação; só access token na elevação; **nunca** em client credentials.
- Endpoint `GET /api/v1/authorization/users/{sub}/session-version`, scope `authorization:read`, sempre 200 com `{ sessionVersion, revoked }`; inexistente/não-Guid/desativado/bloqueado/tenant inativo → `{ null, true }`.
- Revogação: `UpdateSecurityStampAsync` **antes** de `RevokeBySubjectAsync` (autorizações e tokens).
- Chave de configuração do cache: `Secco:Authentication:SessionVersionCacheTtlSeconds`, padrão 60, > 0.
- Verificação fail-closed: falha ao consultar com cache vencido → recusa. Token/cookie sem `sver` → passa.
- `SecureGate:Tokens:AccessTokenLifetimeMinutes` padrão **5**.
- O client `secco-adminportal` **não** ganha client credentials (tem permissão de `securegate:admin`, e o filtro de operador só roda no login). A consulta de versão do AdminPortal usa client próprio `secco-adminportal-sessions`, só `authorization:read`.
- Proibido no SecureGate: `RoleManager<`, `AddToRoleAsync`, `RemoveFromRoleAsync`, `IsInRoleAsync`, `GetUsersInRoleAsync` (guarda de código já existente).
- Contrato mudou → regenerar `openapi.json` na mesma tarefa (`SECCO_UPDATE_OPENAPI=true`).
- C# com tab, Razor com 4 espaços, todos os arquivos CRLF. Warnings = erros em produto.
- Commits direto na `main`, Conventional Commits, terminando com `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`; stage só dos arquivos da tarefa; **nunca empurrar**.
- Verificação final: `dotnet test Secco.Platform.slnx`.
- **NotificationHub fora desta entrega:** ele não usa o SecureGate hoje (sem catálogo remoto nem resolvedor de permissões); a verificação chega junto quando ele adotar o `Secco.SecureGate.Client`.

## Mapa de arquivos

**SharedKernel:** `Constants/SeccoClaims.cs` (mod).

**SecureGate Application** (`src/SecureGate/Secco.SecureGate.Application/`): `Sessions/SessionVersion.cs`, `Sessions/SessionVersionDto.cs`, `Sessions/GetSessionVersionHandler.cs`, `Sessions/ISessionRevoker.cs`, `Sessions/RevokeUserSessionsHandler.cs` (novos); `Users/IUserDirectory.cs`, `Users/SetUserActivationHandler.cs`, `Users/RemoveUserRoleHandler.cs`, `Users/AddUserRoleHandler.cs`, `SecureGateApplicationExtensions.cs` (mod).

**SecureGate Infrastructure:** `Sessions/SessionRevoker.cs` (novo); `Users/UserAccountService.cs`, `SecureGateInfrastructureExtensions.cs`, `Seeding/SecureGateDevelopmentDataSeeder.cs` (mod).

**SecureGate Api:** `Identity/OidcPrincipalBuilder.cs`, `Endpoints/InteractiveEndpoints.cs`, `Endpoints/AuthorizationEndpoints.cs`, `Endpoints/UserEndpoints.cs`, `Extensions/SecureGateOpenIddictExtensions.cs`, `openapi/openapi.json` (mod).

**SDK AspNetCore** (`src/SDK/Secco.SDK.AspNetCore/`): `Authentication/ISessionVersionResolver.cs`, `Authentication/SessionVersionChecker.cs`, `Authentication/SeccoSessionVersionOptions.cs`, `Extensions/SeccoCookieSessionValidationExtensions.cs` (novos); `Authentication/ConfigureSeccoJwtBearerOptions.cs`, `Extensions/SeccoAuthenticationServiceCollectionExtensions.cs` (mod).

**SecureGate.Client:** `Authorization/SecureGateSessionVersionResolver.cs`, `Authorization/SecureGateSessionVersionResolverExtensions.cs` (novos); `Authorization/SecureGatePermissionResolverExtensions.cs` (mod).

**AdminPortal** (`src/AdminPortal/Secco.AdminPortal/`): `Authentication/OperatorSession.cs`, `Authentication/IOperatorSessionStore.cs`, `Authentication/OperatorTokenRefresher.cs` (novos); `Authentication/OperatorTokenProvider.cs`, `Authentication/AdminPortalAuthenticationExtensions.cs`, `Authentication/AdminPortalDefaults.cs`, `Program.cs`, `appsettings.Development.json` (mod).

**Testes:** `tests/SharedKernel/.../SeccoClaimsTests.cs` (se existir, senão sem teste — constante); `tests/SDK/Secco.SDK.AspNetCore.Tests/Authentication/SessionVersionValidationTests.cs`, `.../CookieSessionValidationTests.cs`; `tests/SecureGate/Secco.SecureGate.Tests/Unit/SessionVersionTests.cs`, `Integration/SessionVersionTokenTests.cs`, `Integration/SessionRevocationEffectTests.cs`, `Integration/CrossProductSessionRevocationTests.cs`, `Integration/OidcLoginDriver.cs` (mod); `tests/AdminPortal/Secco.AdminPortal.Tests/OperatorTokenProviderTests.cs` (reescrito).

---

### Task 1: Claim `sver` no SharedKernel e nos tokens do SecureGate; access token de 5 minutos

**Files:**
- Modify: `src/SharedKernel/Secco.SharedKernel/Constants/SeccoClaims.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Sessions/SessionVersion.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Identity/OidcPrincipalBuilder.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Extensions/SecureGateOpenIddictExtensions.cs:34`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/SessionVersionTests.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/SessionVersionTokenTests.cs`

**Interfaces:**
- Produces: `SeccoClaims.SessionVersion = "sver"`; `SessionVersion.From(string? securityStamp) : string` (16 caracteres Base64Url).

- [ ] **Step 1: Testes que falham**

`tests/SecureGate/Secco.SecureGate.Tests/Unit/SessionVersionTests.cs`:

```csharp
using FluentAssertions;
using Secco.SecureGate.Application.Sessions;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>A versão de sessão resume o SecurityStamp sem expô-lo.</summary>
public class SessionVersionTests
{
	[Fact]
	public void MesmoStamp_MesmaVersao() =>
		SessionVersion.From("ABC").Should().Be(SessionVersion.From("ABC"));

	[Fact]
	public void StampDiferente_VersaoDiferente() =>
		SessionVersion.From("ABC").Should().NotBe(SessionVersion.From("ABD"));

	[Fact]
	public void Formato_16CaracteresBase64UrlSemOStamp()
	{
		var version = SessionVersion.From("STAMP-SECRETO-1234");

		version.Should().HaveLength(16).And.MatchRegex("^[A-Za-z0-9_-]{16}$").And.NotContain("STAMP");
	}

	[Fact]
	public void StampNulo_TemVersaoEstavel() =>
		SessionVersion.From(null).Should().Be(SessionVersion.From(string.Empty));
}
```

`tests/SecureGate/Secco.SecureGate.Tests/Integration/SessionVersionTokenTests.cs`:

```csharp
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
```

Acrescentar em `TokenExchangeElevationTests` (seção "o caso que deve funcionar"), reusando `LoginAsync`, `GrantAsync` e `ExchangeAsync` da classe:

```csharp
	[Fact]
	public async Task Exchange_TokenElevadoLevaVersaoDeSessao()
	{
		var (accessToken, _) = await LoginAsync();
		await GrantAsync();

		var result = await ExchangeAsync(accessToken);

		new JsonWebTokenHandler().ReadJsonWebToken(result.AccessToken).TryGetClaim("sver", out _)
			.Should().BeTrue("revogar a sessão precisa derrubar também a elevação");
	}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~SessionVersionTests|FullyQualifiedName~SessionVersionTokenTests|FullyQualifiedName~Exchange_TokenElevadoLevaVersaoDeSessao"`
Expected: erro de compilação (`SessionVersion` não existe).

- [ ] **Step 3: Implementar**

`SeccoClaims.cs`, depois de `Scope`:

```csharp
	/// <summary>
	/// Versão da sessão em que o token foi emitido (ADR-0032). Divergir da versão atual do usuário
	/// significa sessão revogada.
	/// </summary>
	public const string SessionVersion = "sver";
```

`src/SecureGate/Secco.SecureGate.Application/Sessions/SessionVersion.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Secco.SecureGate.Application.Sessions;

/// <summary>
/// Versão de sessão (ADR-0032): resumo do <c>SecurityStamp</c> do Identity. Nunca o stamp cru — ele
/// participa da geração dos tokens de recuperação de senha.
/// </summary>
public static class SessionVersion
{
	/// <summary>Tamanho da versão, em caracteres Base64Url (96 bits).</summary>
	public const int Length = 16;

	/// <summary>Deriva a versão a partir do stamp atual.</summary>
	/// <param name="securityStamp">SecurityStamp do usuário.</param>
	public static string From(string? securityStamp)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp ?? string.Empty));

		return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')[..Length];
	}
}
```

`OidcPrincipalBuilder.ForUser`: depois de `identity.SetClaims(SeccoClaims.Role, [.. roleList]);` acrescentar:

```csharp
		identity.SetClaim(SeccoClaims.SessionVersion, SessionVersion.From(user.SecurityStamp));
```

`OidcPrincipalBuilder.ForElevation`: depois de `identity.SetClaim(SecureGatePlatform.TokenExchangeClaim, ...)`:

```csharp
		// Revogar a sessão de quem elevou derruba também o token elevado (ADR-0032)
		identity.SetClaim(SeccoClaims.SessionVersion, SessionVersion.From(user.SecurityStamp));
```

`OidcPrincipalBuilder.GetDestinations`: logo no início do método:

```csharp
		// sver nos dois: o access token para os produtos, o id_token para as aplicações de cookie (ADR-0032)
		if (claim.Type == SeccoClaims.SessionVersion)
		{
			return [Destinations.AccessToken, Destinations.IdentityToken];
		}
```

(acrescentar `using Secco.SecureGate.Application.Sessions;`.)

`SecureGateOpenIddictExtensions.cs:34`: trocar `configuration.GetValue("SecureGate:Tokens:AccessTokenLifetimeMinutes", 60)` por `configuration.GetValue("SecureGate:Tokens:AccessTokenLifetimeMinutes", 5)`, com o comentário acima:

```csharp
		// ADR-0032: 5 minutos por padrão — segunda barreira da revogação, depois da versão de sessão
```

- [ ] **Step 4: Rodar e ver passar**

Mesmo comando do Step 2 → PASS. Depois: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release` → PASS (nenhum teste existente assume 60 minutos).

- [ ] **Step 5: Commit**

```bash
git add src/SharedKernel src/SecureGate tests/SecureGate
git commit -m "feat(securegate): claim sver nos tokens de usuário e access token de 5 minutos (ADR-0032)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Endpoint de versão de sessão

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Sessions/SessionVersionDto.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Sessions/GetSessionVersionHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/IUserDirectory.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Users/UserAccountService.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/AuthorizationEndpoints.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/openapi/openapi.json` (regenerado)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/SessionRevocationEffectTests.cs` (criado aqui; Tasks 3 e 4 acrescentam)

**Interfaces:**
- Consumes: `SessionVersion.From` (Task 1); `UserStatuses.From` (existente); `ITenantRepository.GetByIdAsync`.
- Produces: `SessionVersionDto(string? SessionVersion, bool Revoked)` com `SessionVersionDto.RevokedState`; `UserSessionState(Guid TenantId, string? SecurityStamp, bool LockoutEnabled, DateTimeOffset? LockoutEnd)`; `IUserDirectory.GetSessionStateAsync(Guid userId, CancellationToken) : Task<UserSessionState?>`; `GetSessionVersionHandler.HandleAsync(string? subject, CancellationToken) : Task<SessionVersionDto>`; rota `GetSessionVersion` (client: `GetSessionVersionAsync(string sub)`).

- [ ] **Step 1: Testes que falham**

`tests/SecureGate/Secco.SecureGate.Tests/Integration/SessionRevocationEffectTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Versão de sessão e efeito da revogação (ADR-0032), sobre a API compartilhada (HS256).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class SessionRevocationEffectTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"sessao-{Guid.NewGuid():N}@secco.test";

	private HttpClient ResolverClient()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.AuthorizationRead));

		return client;
	}

	private Task<JsonElement> GetVersionAsync(string subject) =>
		ResolverClient().GetFromJsonAsync<JsonElement>($"/api/v1/authorization/users/{subject}/session-version", Json);

	private async Task<string> StampVersionAsync(Guid userId)
	{
		using var scope = factory.Services.CreateScope();
		var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString());

		return SessionVersion.From(user!.SecurityStamp);
	}

	[Fact]
	public async Task Versao_UsuarioAtivo_DevolveAVersaoAtual()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		var version = await GetVersionAsync(userId.ToString());

		version.GetProperty("revoked").GetBoolean().Should().BeFalse();
		version.GetProperty("sessionVersion").GetString().Should().Be(await StampVersionAsync(userId));
	}

	[Fact]
	public async Task Versao_UsuarioDesativado_Revogado()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await IdentitySeed.DeactivateAsync(factory, userId);

		(await GetVersionAsync(userId.ToString())).GetProperty("revoked").GetBoolean().Should().BeTrue();
	}

	[Fact]
	public async Task Versao_UsuarioBloqueadoPorTentativas_Revogado()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await IdentitySeed.LockOutAsync(factory, userId, DateTimeOffset.UtcNow.AddMinutes(5));

		(await GetVersionAsync(userId.ToString())).GetProperty("revoked").GetBoolean().Should().BeTrue();
	}

	[Fact]
	public async Task Versao_TenantInativo_Revogado()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		using (var scope = factory.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			(await context.Tenants.SingleAsync(t => t.Id == _tenantId)).Deactivate();
			await context.SaveChangesAsync();
		}

		(await GetVersionAsync(userId.ToString())).GetProperty("revoked").GetBoolean().Should().BeTrue();
	}

	[Theory]
	[InlineData("nao-e-guid")]
	[InlineData("0192e0a0-0000-7000-8000-000000000000")]
	public async Task Versao_SubjectInexistenteOuInvalido_RevogadoSemDistinguir(string subject)
	{
		var version = await GetVersionAsync(subject);

		version.GetProperty("revoked").GetBoolean().Should().BeTrue();
		version.GetProperty("sessionVersion").ValueKind.Should().Be(JsonValueKind.Null);
	}

	[Fact]
	public async Task Versao_SemToken_401() =>
		(await factory.CreateClient().GetAsync($"/api/v1/authorization/users/{Guid.CreateVersion7()}/session-version"))
			.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

	[Fact]
	public async Task Versao_SemScopeAuthorizationRead_403() =>
		(await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/authorization/users/{Guid.CreateVersion7()}/session-version"))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~SessionRevocationEffectTests"`
Expected: FAIL — rota inexistente.

- [ ] **Step 3: Implementar**

`Sessions/SessionVersionDto.cs`:

```csharp
namespace Secco.SecureGate.Application.Sessions;

/// <summary>Versão de sessão atual de um usuário, consultada pelos produtos (ADR-0032).</summary>
/// <param name="SessionVersion">Versão atual; nula quando revogada.</param>
/// <param name="Revoked">Sessão não pode ser aceita: conta inexistente, desativada, bloqueada ou de tenant inativo.</param>
public sealed record SessionVersionDto(string? SessionVersion, bool Revoked)
{
	/// <summary>Resposta única para todo caso recusado — não distingue inexistente de desativado.</summary>
	public static readonly SessionVersionDto RevokedState = new(null, true);
}
```

`IUserDirectory.cs`, antes da interface:

```csharp
/// <summary>Estado da conta relevante para a sessão.</summary>
/// <param name="TenantId">Tenant.</param>
/// <param name="SecurityStamp">SecurityStamp atual.</param>
/// <param name="LockoutEnabled">Lockout habilitado.</param>
/// <param name="LockoutEnd">Fim do bloqueio.</param>
public sealed record UserSessionState(Guid TenantId, string? SecurityStamp, bool LockoutEnabled, DateTimeOffset? LockoutEnd);
```

e na interface:

```csharp
	/// <summary>Estado de sessão do usuário, em qualquer tenant; <c>null</c> se não existe.</summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<UserSessionState?> GetSessionStateAsync(Guid userId, CancellationToken cancellationToken = default);
```

`UserAccountService.cs`:

```csharp
	public Task<UserSessionState?> GetSessionStateAsync(Guid userId, CancellationToken cancellationToken = default) =>
		context.Users
			.AsNoTracking()
			.Where(user => user.Id == userId)
			.Select(user => new UserSessionState(user.TenantId, user.SecurityStamp, user.LockoutEnabled, user.LockoutEnd))
			.FirstOrDefaultAsync(cancellationToken);
```

`Sessions/GetSessionVersionHandler.cs`:

```csharp
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Sessions;

/// <summary>
/// Versão de sessão atual (ADR-0032). Sempre responde: todo caso que não pode ser aceito vira o mesmo
/// estado revogado, para a consulta não servir de oráculo de existência de conta (ADR-0020).
/// </summary>
public sealed class GetSessionVersionHandler(IUserDirectory userDirectory, ITenantRepository tenants)
{
	/// <summary>Executa a consulta.</summary>
	/// <param name="subject">O <c>sub</c> do token.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<SessionVersionDto> HandleAsync(string? subject, CancellationToken cancellationToken = default)
	{
		if (!Guid.TryParse(subject, out var userId))
		{
			return SessionVersionDto.RevokedState;
		}

		var state = await userDirectory.GetSessionStateAsync(userId, cancellationToken).ConfigureAwait(false);

		if (state is null
			|| UserStatuses.From(state.LockoutEnabled, state.LockoutEnd, DateTimeOffset.UtcNow) != UserStatuses.Active)
		{
			return SessionVersionDto.RevokedState;
		}

		var tenant = await tenants.GetByIdAsync(state.TenantId, cancellationToken).ConfigureAwait(false);

		return tenant is { IsActive: true }
			? new SessionVersionDto(SessionVersion.From(state.SecurityStamp), Revoked: false)
			: SessionVersionDto.RevokedState;
	}
}
```

`SecureGateApplicationExtensions.cs`: `services.AddScoped<Sessions.GetSessionVersionHandler>();`.

`AuthorizationEndpoints.cs`: acrescentar `using Secco.SecureGate.Application.Sessions;` e, antes de `return endpoints;`:

```csharp
		endpoints.MapGet("/api/v1/authorization/users/{sub}/session-version", async (
				string sub,
				GetSessionVersionHandler handler,
				CancellationToken cancellationToken) =>
			Results.Ok(await handler.HandleAsync(sub, cancellationToken)))
			.WithTags("Authorization")
			.WithName("GetSessionVersion")
			.WithSummary("Versão de sessão atual do usuário (ADR-0032); revogado para conta inexistente, desativada, bloqueada ou de tenant inativo.")
			.Produces<SessionVersionDto>(StatusCodes.Status200OK)
			.RequireAuthorization(policy =>
				policy.RequireAssertion(context =>
					ScopeAuthorization.HasScope(context.User, SecureGateScopes.AuthorizationRead)));
```

Regenerar o contrato (Git Bash): `SECCO_UPDATE_OPENAPI=true dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~OpenApiContractTests"`; conferir `git diff -- src/SecureGate/Secco.SecureGate.Api/openapi/openapi.json | grep "^-" | grep -v "^---"` sem remoções.

- [ ] **Step 4: Rodar e ver passar** — comando do Step 2 → PASS (8 casos).

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate tests/SecureGate
git commit -m "feat(securegate): endpoint de versão de sessão para os produtos (ADR-0032)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Revogação única e gatilhos

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Sessions/ISessionRevoker.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Sessions/RevokeUserSessionsHandler.cs`
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/Sessions/SessionRevoker.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/IUserDirectory.cs` (enum `RoleAssignmentOutcome`)
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/AddUserRoleHandler.cs` (`ToResult`)
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/RemoveUserRoleHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/SetUserActivationHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Users/UserAccountService.cs` (`RemoveRoleAsync`)
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/SecureGateInfrastructureExtensions.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/UserEndpoints.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/openapi/openapi.json`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/SessionRevocationEffectTests.cs`

**Interfaces:**
- Consumes: `SessionVersion.From` (Task 1); `SessionRevocationEffectTests` helpers (Task 2).
- Produces: `enum SessionRevocationReason { AdminRequest, UserDeactivated, RoleRemoved }`; `ISessionRevoker.RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken) : Task`; `RoleAssignmentOutcome.NotAssigned`; `RevokeUserSessionsHandler.HandleAsync(Guid tenantId, Guid userId, CancellationToken) : Task<Result>`; rota `RevokeUserSessions`.

- [ ] **Step 1: Testes que falham**

Acrescentar a `SessionRevocationEffectTests`:

```csharp
	private string RevokeUrl(Guid userId) => $"/api/v1/tenants/{_tenantId}/users/{userId}/sessions/revoke";

	[Fact]
	public async Task RevokeUserSessions_TrocaAVersao()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var before = await StampVersionAsync(userId);

		(await IdentitySeed.AdminClient(factory).PostAsync(RevokeUrl(userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await StampVersionAsync(userId)).Should().NotBe(before);
	}

	[Fact]
	public async Task RevokeUserSessions_UsuarioDeOutroTenant_404ENaoTroca()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		var stranger = await IdentitySeed.UserAsync(factory, otherTenant, Email());
		var before = await StampVersionAsync(stranger);

		(await IdentitySeed.AdminClient(factory).PostAsync(RevokeUrl(stranger), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

		(await StampVersionAsync(stranger)).Should().Be(before);
	}

	[Fact]
	public async Task RevokeUserSessions_SemToken_401_SemScope_403()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		(await factory.CreateClient().PostAsync(RevokeUrl(userId), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		(await ResolverClient().PostAsync(RevokeUrl(userId), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Desativar_TrocaAVersao()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var before = await StampVersionAsync(userId);

		(await IdentitySeed.AdminClient(factory).PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/deactivate", null))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await StampVersionAsync(userId)).Should().NotBe(before);
	}

	[Fact]
	public async Task RemoverPerfil_Efetivo_TrocaAVersao_Repetido_NaoTroca()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email(), "leitor");
		var admin = IdentitySeed.AdminClient(factory);
		var url = $"/api/v1/tenants/{_tenantId}/users/{userId}/roles/leitor";
		var before = await StampVersionAsync(userId);

		(await admin.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		var afterRemoval = await StampVersionAsync(userId);
		(await admin.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		afterRemoval.Should().NotBe(before);
		(await StampVersionAsync(userId)).Should().Be(afterRemoval, "remoção repetida não pode derrubar a sessão à toa");
	}
```

Acrescentar a `SessionRevocationTests` (coleção auto-validada, tokens reais) — prova que a revogação atinge o refresh **independentemente** do lockout:

```csharp
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
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~SessionRevocationEffectTests|FullyQualifiedName~SessionRevocationTests"`
Expected: FAIL nos testes novos (rota inexistente; stamp não muda; refresh aceito depois de reativar).

- [ ] **Step 3: Implementar**

`Sessions/ISessionRevoker.cs`:

```csharp
namespace Secco.SecureGate.Application.Sessions;

/// <summary>Motivo registrado da revogação.</summary>
public enum SessionRevocationReason
{
	/// <summary>Admin pediu para encerrar as sessões.</summary>
	AdminRequest,

	/// <summary>Conta desativada.</summary>
	UserDeactivated,

	/// <summary>Usuário retirado de um perfil.</summary>
	RoleRemoved,
}

/// <summary>Encerra todas as sessões de um usuário (ADR-0032).</summary>
public interface ISessionRevoker
{
	/// <summary>
	/// Troca a versão de sessão e revoga autorizações e tokens. Usuário inexistente não é erro: não há
	/// sessão a encerrar.
	/// </summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="reason">Motivo, para o log.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken cancellationToken = default);
}
```

`Infrastructure/Sessions/SessionRevoker.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Infrastructure.Sessions;

/// <summary>
/// Revogação única (ADR-0032). A ordem é a segurança: o stamp muda antes da revogação no OpenIddict, então
/// uma falha no meio deixa a conta mais fechada — os produtos já recusam a versão antiga.
/// </summary>
internal sealed partial class SessionRevoker(
	UserManager<User> userManager,
	IOpenIddictAuthorizationManager authorizations,
	IOpenIddictTokenManager tokens,
	ILogger<SessionRevoker> logger) : ISessionRevoker
{
	public async Task RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

		if (user is null)
		{
			return;
		}

		var stamped = await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false);

		if (!stamped.Succeeded)
		{
			throw new InvalidOperationException(
				"O Identity recusou a troca do security stamp: " + string.Join(", ", stamped.Errors.Select(e => e.Code)));
		}

		var subject = userId.ToString();
		await authorizations.RevokeBySubjectAsync(subject, cancellationToken).ConfigureAwait(false);
		await tokens.RevokeBySubjectAsync(subject, cancellationToken).ConfigureAwait(false);

		LogRevoked(logger, userId, user.TenantId, reason);
	}

	[LoggerMessage(EventId = 3201, Level = LogLevel.Information,
		Message = "Sessões revogadas: usuário {UserId}, tenant {TenantId}, motivo {Reason}.")]
	private static partial void LogRevoked(ILogger logger, Guid userId, Guid tenantId, SessionRevocationReason reason);
}
```

`SecureGateInfrastructureExtensions.cs`, junto dos outros `AddScoped` da Application: `services.AddScoped<Application.Sessions.ISessionRevoker, Sessions.SessionRevoker>();`.

`Sessions/RevokeUserSessionsHandler.cs`:

```csharp
using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Sessions;

/// <summary>"Encerrar sessões" pelo admin (ADR-0032). Usuário de outro tenant responde como inexistente.</summary>
public sealed class RevokeUserSessionsHandler(IUserDirectory userDirectory, ISessionRevoker revoker)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		if (!await userDirectory.BelongsToTenantAsync(tenantId, userId, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		await revoker.RevokeAllAsync(userId, SessionRevocationReason.AdminRequest, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
```

`IUserDirectory.cs` — no enum `RoleAssignmentOutcome`, depois de `RoleNotFound`:

```csharp

	/// <summary>Remoção sem efeito: o usuário não era membro do perfil.</summary>
	NotAssigned,
```

`AddUserRoleHandler.ToResult` — acrescentar o caso antes do `_`:

```csharp
		RoleAssignmentOutcome.NotAssigned => Result.Success(),
```

`UserAccountService.RemoveRoleAsync` — trocar o final (a partir de `if (assignment is not null)`) por:

```csharp
		if (assignment is null)
		{
			return RoleAssignmentOutcome.NotAssigned;
		}

		context.UserRoles.Remove(assignment);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return RoleAssignmentOutcome.Done;
```

`RemoveUserRoleHandler` — o construtor passa a `RemoveUserRoleHandler(IUserDirectory userDirectory, ISessionRevoker revoker)` (`using Secco.SecureGate.Application.Sessions;`) e o final do `HandleAsync` troca `return AddUserRoleHandler.ToResult(await userDirectory.RemoveRoleAsync(...))` por:

```csharp
		var outcome = await userDirectory
			.RemoveRoleAsync(command.TenantId, command.UserId, name, cancellationToken)
			.ConfigureAwait(false);

		// Só uma remoção de fato encerra a sessão; a idempotente não derruba ninguém à toa
		if (outcome == RoleAssignmentOutcome.Done)
		{
			await revoker.RevokeAllAsync(command.UserId, SessionRevocationReason.RoleRemoved, cancellationToken)
				.ConfigureAwait(false);
		}

		return AddUserRoleHandler.ToResult(outcome);
```

`SetUserActivationHandler` — construtor `SetUserActivationHandler(IUserDirectory userDirectory, ISessionRevoker revoker)`; trocar o final:

```csharp
		if (!found)
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		// Desativar revoga na hora, sem esperar a renovação encontrar o lockout (ADR-0032)
		if (!command.Active)
		{
			await revoker.RevokeAllAsync(command.UserId, SessionRevocationReason.UserDeactivated, cancellationToken)
				.ConfigureAwait(false);
		}

		return Result.Success();
```

(a linha anterior `return found ? Result.Success() : Result.Failure(SecureGateErrors.Users.NotFound);` sai.)

`SecureGateApplicationExtensions.cs`: `services.AddScoped<Sessions.RevokeUserSessionsHandler>();`.

`UserEndpoints.cs`, antes de `return endpoints;`:

```csharp
		group.MapPost("/{userId:guid}/sessions/revoke", async (
				Guid tenantId,
				Guid userId,
				RevokeUserSessionsHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("RevokeUserSessions")
			.WithSummary("Encerra todas as sessões do usuário: produtos recusam os tokens atuais em até um TTL de cache (ADR-0032).")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);
```

(`using Secco.SecureGate.Application.Sessions;`.)

Regenerar o contrato como na Task 2.

- [ ] **Step 4: Rodar e ver passar**

Comando do Step 2 → PASS. Depois a suíte inteira do SecureGate → PASS (`LastOperatorGuardTests` e demais seguem verdes).

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate tests/SecureGate
git commit -m "feat(securegate): revogação única de sessões e seus gatilhos (ADR-0032)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: `/connect/authorize` valida o cookie contra o stamp e o estado da conta

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/InteractiveEndpoints.cs`
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Integration/OidcLoginDriver.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/SessionRevocationTests.cs`

**Interfaces:**
- Consumes: `ISessionRevoker` (Task 3); `AccountStateGuard.CanReceiveTokensAsync` (existente).
- Produces (testes): `OidcLoginDriver.AuthorizeAsync(HttpClient browser, string scope) : Task<HttpResponseMessage>`.

- [ ] **Step 1: Teste que falha**

`OidcLoginDriver` — novo método (reusa a montagem de URL do `SubmitLoginAsync`; extrair a montagem para um método privado `AuthorizeUrl(string scope, string challenge)` e usá-lo nos dois):

```csharp
	/// <summary>Chama o authorize com o cookie que o navegador já tem, sem passar pelo formulário.</summary>
	public Task<HttpResponseMessage> AuthorizeAsync(HttpClient browser, string scope) =>
		browser.GetAsync(AuthorizeUrl(scope, CreatePkce().Challenge));

	private string AuthorizeUrl(string scope, string challenge) =>
		QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
		{
			["response_type"] = "code",
			["client_id"] = clientId,
			["redirect_uri"] = redirectUri,
			["scope"] = scope,
			["code_challenge"] = challenge,
			["code_challenge_method"] = "S256",
			["state"] = Guid.NewGuid().ToString("N"),
			["nonce"] = Guid.NewGuid().ToString("N"),
		});
```

`SessionRevocationTests`:

```csharp
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
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~Cookie_DepoisDeEncerrarSessoes_NaoEmiteNovoCode"`
Expected: FAIL — o segundo authorize ainda redireciona com code.

- [ ] **Step 3: Implementar**

`InteractiveEndpoints.AuthorizeAsync` — assinatura passa a `(HttpContext context, UserManager<User> userManager, SignInManager<User> signInManager, IOpenIddictScopeManager scopeManager)`. Substituir o bloco

```csharp
		var user = await userManager.GetUserAsync(authentication.Principal!);

		if (user is null)
		{
			...
		}
```

(o `if` que devolve `Results.Challenge` com comentário "Cookie válido mas usuário sumiu") por:

```csharp
		// ADR-0032: o cookie só vale se o stamp ainda é o do login e a conta pode receber token. Sem isto, um
		// cookie roubado geraria tokens novos — já com a versão nova — depois de qualquer revogação.
		var user = await signInManager.ValidateSecurityStampAsync(authentication.Principal);

		if (user is null
			|| !await AccountStateGuard.CanReceiveTokensAsync(
				user,
				userManager,
				signInManager,
				context.RequestServices.GetRequiredService<ITenantRepository>(),
				context.RequestAborted))
		{
			await context.SignOutAsync(IdentityConstants.ApplicationScheme);

			return Results.Challenge(
				new AuthenticationProperties
				{
					RedirectUri = context.Request.PathBase + context.Request.Path + context.Request.QueryString,
				},
				[IdentityConstants.ApplicationScheme]);
		}
```

(`using Secco.SecureGate.Application.Tenants;`.)

- [ ] **Step 4: Rodar e ver passar**

Comando do Step 2 → PASS; depois `FullyQualifiedName~SessionRevocationTests|FullyQualifiedName~AuthorizationCodeFlowTests|FullyQualifiedName~OperatorScopeFilterTests|FullyQualifiedName~TokenExchangeElevationTests|FullyQualifiedName~ProfileAssignmentTokenTests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Api/Endpoints/InteractiveEndpoints.cs tests/SecureGate
git commit -m "fix(securegate): authorize valida o cookie contra o security stamp e o estado da conta

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Verificação da versão de sessão no SDK (JwtBearer)

**Files:**
- Create: `src/SDK/Secco.SDK.AspNetCore/Authentication/ISessionVersionResolver.cs`
- Create: `src/SDK/Secco.SDK.AspNetCore/Authentication/SeccoSessionVersionOptions.cs`
- Create: `src/SDK/Secco.SDK.AspNetCore/Authentication/SessionVersionChecker.cs`
- Modify: `src/SDK/Secco.SDK.AspNetCore/Authentication/ConfigureSeccoJwtBearerOptions.cs`
- Modify: `src/SDK/Secco.SDK.AspNetCore/Extensions/SeccoAuthenticationServiceCollectionExtensions.cs`
- Test: `tests/SDK/Secco.SDK.AspNetCore.Tests/Authentication/SessionVersionValidationTests.cs`

**Interfaces:**
- Consumes: `SeccoClaims.SessionVersion` (Task 1).
- Produces: `SessionVersionStatus(string? SessionVersion, bool Revoked)`; `ISessionVersionResolver { bool IsEnabled { get; } ValueTask<SessionVersionStatus> ResolveAsync(string subject, CancellationToken) }`; `SeccoSessionVersionOptions { int SessionVersionCacheTtlSeconds = 60 }` (seção `Secco:Authentication`); `SessionVersionChecker.IsCurrentAsync(ClaimsPrincipal principal, CancellationToken) : ValueTask<bool>` (public sealed, para a Task 6 usar); `SeccoAuthenticationServiceCollectionExtensions.AddSeccoSessionVersionChecking(IServiceCollection)` (internal, chamada por `AddSeccoAuthentication` e pela Task 6).

- [ ] **Step 1: Testes que falham**

`tests/SDK/Secco.SDK.AspNetCore.Tests/Authentication/SessionVersionValidationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Secco.SDK.AspNetCore.Authentication;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Authentication;

/// <summary>O produto recusa token de sessão revogada (ADR-0032), com cache e fail-closed.</summary>
public class SessionVersionValidationTests : IAsyncLifetime
{
	private const string SigningKey = "chave-de-testes-com-32-caracteres!!";
	private const string Issuer = "secco-tests";
	private const string Audience = "secco-tests";

	private readonly FakeResolver _resolver = new();
	private IHost _host = null!;

	public sealed class FakeResolver : ISessionVersionResolver
	{
		public bool IsEnabled { get; set; } = true;

		public SessionVersionStatus Status { get; set; } = new("v1", false);

		public bool Throw { get; set; }

		public int Calls { get; private set; }

		public ValueTask<SessionVersionStatus> ResolveAsync(string subject, CancellationToken cancellationToken = default)
		{
			Calls++;

			return Throw ? throw new HttpRequestException("SecureGate fora") : ValueTask.FromResult(Status);
		}
	}

	public async Task InitializeAsync()
	{
		_host = await new HostBuilder()
			.ConfigureWebHost(web =>
			{
				web.UseTestServer();
				web.UseEnvironment(Environments.Development);
				web.ConfigureAppConfiguration((_, configuration) =>
					configuration.AddInMemoryCollection(new Dictionary<string, string?>
					{
						["Secco:Authentication:Audience"] = Audience,
						["Secco:Authentication:Issuer"] = Issuer,
						["Secco:Authentication:DevelopmentSigningKey"] = SigningKey,
						["Secco:Authentication:SessionVersionCacheTtlSeconds"] = "60",
					}));
				web.ConfigureServices(services =>
				{
					services.AddRouting();
					services.AddSeccoPlatform();
					services.AddSingleton<ISessionVersionResolver>(_resolver);
				});
				web.Configure(app =>
				{
					app.UseRouting();
					app.UseSeccoPlatform();
					app.UseEndpoints(endpoints => endpoints.MapGet("/protegido", () => "ok"));
				});
			})
			.StartAsync();
	}

	public async Task DisposeAsync()
	{
		await _host.StopAsync();
		_host.Dispose();
	}

	private static string Token(string subject, string? sessionVersion)
	{
		var claims = new Dictionary<string, object> { [SeccoClaims.Subject] = subject };

		if (sessionVersion is not null)
		{
			claims[SeccoClaims.SessionVersion] = sessionVersion;
		}

		return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
		{
			Issuer = Issuer,
			Audience = Audience,
			Claims = claims,
			Expires = DateTime.UtcNow.AddMinutes(5),
			SigningCredentials = new SigningCredentials(
				new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256),
		});
	}

	private Task<HttpResponseMessage> GetAsync(string token)
	{
		var client = _host.GetTestClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

		return client.GetAsync("/protegido");
	}

	private static string Subject() => Guid.NewGuid().ToString();

	[Fact]
	public async Task VersaoIgual_Passa() =>
		(await GetAsync(Token(Subject(), "v1"))).StatusCode.Should().Be(HttpStatusCode.OK);

	[Fact]
	public async Task VersaoDiferente_401()
	{
		_resolver.Status = new SessionVersionStatus("v2", false);

		(await GetAsync(Token(Subject(), "v1"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task Revogado_401()
	{
		_resolver.Status = new SessionVersionStatus(null, true);

		(await GetAsync(Token(Subject(), "v1"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task SemSver_PassaSemConsultar()
	{
		(await GetAsync(Token(Subject(), null))).StatusCode.Should().Be(HttpStatusCode.OK);

		_resolver.Calls.Should().Be(0);
	}

	[Fact]
	public async Task ResolverDesabilitado_PassaSemConsultar()
	{
		_resolver.IsEnabled = false;
		_resolver.Status = new SessionVersionStatus(null, true);

		(await GetAsync(Token(Subject(), "v1"))).StatusCode.Should().Be(HttpStatusCode.OK);
		_resolver.Calls.Should().Be(0);
	}

	[Fact]
	public async Task ResolverFalha_FailClosed401()
	{
		_resolver.Throw = true;

		(await GetAsync(Token(Subject(), "v1"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task DuasRequisicoesNoTtl_UmaConsulta()
	{
		var subject = Subject();

		(await GetAsync(Token(subject, "v1"))).StatusCode.Should().Be(HttpStatusCode.OK);
		(await GetAsync(Token(subject, "v1"))).StatusCode.Should().Be(HttpStatusCode.OK);

		_resolver.Calls.Should().Be(1);
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SDK/Secco.SDK.AspNetCore.Tests/Secco.SDK.AspNetCore.Tests.csproj -c Release --filter "FullyQualifiedName~SessionVersionValidationTests"`
Expected: erro de compilação.

- [ ] **Step 3: Implementar**

`Authentication/ISessionVersionResolver.cs`:

```csharp
namespace Secco.SDK.AspNetCore.Authentication;

/// <summary>Versão de sessão atual de um usuário (ADR-0032).</summary>
/// <param name="SessionVersion">Versão atual; nula quando revogada.</param>
/// <param name="Revoked">Sessão não pode ser aceita.</param>
public sealed record SessionVersionStatus(string? SessionVersion, bool Revoked);

/// <summary>
/// Fonte da versão de sessão atual — em produção, o SecureGate via <c>Secco.SecureGate.Client</c>.
/// </summary>
public interface ISessionVersionResolver
{
	/// <summary>
	/// Indica se a verificação está ativa. Falso só quando a fonte não está configurada (DEV standalone);
	/// uma fonte configurada que falha continua habilitada e recusa (fail-closed).
	/// </summary>
	bool IsEnabled { get; }

	/// <summary>Consulta a versão atual.</summary>
	/// <param name="subject">O <c>sub</c> do token.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	ValueTask<SessionVersionStatus> ResolveAsync(string subject, CancellationToken cancellationToken = default);
}
```

`Authentication/SeccoSessionVersionOptions.cs`:

```csharp
namespace Secco.SDK.AspNetCore.Authentication;

/// <summary>Opções da verificação de versão de sessão (seção <c>Secco:Authentication</c>, ADR-0032).</summary>
public sealed class SeccoSessionVersionOptions
{
	/// <summary>TTL do cache por usuário, em segundos. Janela máxima entre revogar e recusar.</summary>
	public int SessionVersionCacheTtlSeconds { get; set; } = 60;
}
```

`Authentication/SessionVersionChecker.cs`:

```csharp
using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SharedKernel.Constants;

namespace Secco.SDK.AspNetCore.Authentication;

/// <summary>
/// Confere a <c>sver</c> de um principal contra a versão atual (ADR-0032): cache por usuário com TTL curto e
/// fail-closed — mesma postura do cache de permissões da ADR-0021.
/// </summary>
public sealed partial class SessionVersionChecker(
	IServiceProvider serviceProvider,
	IOptions<SeccoSessionVersionOptions> options,
	ILogger<SessionVersionChecker> logger)
{
	private sealed record Entry(SessionVersionStatus Status, DateTimeOffset FreshUntil);

	private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

	/// <summary>Indica se a sessão do principal ainda vale.</summary>
	/// <param name="principal">Principal autenticado (token ou cookie).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async ValueTask<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(principal);

		// Sem sver: emitido antes da ADR-0032, ou token de máquina. A ausência não é forjável (assinatura).
		if (principal.FindFirst(SeccoClaims.SessionVersion)?.Value is not { Length: > 0 } presented)
		{
			return true;
		}

		var resolver = serviceProvider.GetService<ISessionVersionResolver>();

		if (resolver is not { IsEnabled: true })
		{
			return true;
		}

		if (principal.FindFirst(SeccoClaims.Subject)?.Value is not { Length: > 0 } subject)
		{
			return false;
		}

		var now = DateTimeOffset.UtcNow;

		if (!_entries.TryGetValue(subject, out var entry) || now >= entry.FreshUntil)
		{
			try
			{
				var status = await resolver.ResolveAsync(subject, cancellationToken).ConfigureAwait(false);
				var ttl = TimeSpan.FromSeconds(Math.Max(1, options.Value.SessionVersionCacheTtlSeconds));
				entry = new Entry(status, now + ttl);
				_entries[subject] = entry;
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				// Fail-closed: sem confirmar a versão, a sessão não é aceita. Nunca loga o token.
				LogResolutionFailed(logger, exception);
				return false;
			}
		}

		return !entry.Status.Revoked && string.Equals(entry.Status.SessionVersion, presented, StringComparison.Ordinal);
	}

	[LoggerMessage(EventId = 1301, Level = LogLevel.Warning,
		Message = "Versão de sessão indisponível; requisição recusada (fail-closed).")]
	private static partial void LogResolutionFailed(ILogger logger, Exception exception);
}
```

(Antes de usar `EventId = 1301`, conferir com `grep -rn "EventId = 13" src/SDK` que não colide; se colidir, usar o próximo livre.)

`SeccoAuthenticationServiceCollectionExtensions.cs` — em `AddSeccoAuthentication`, antes de `services.AddAuthentication(...)`: `services.AddSeccoSessionVersionChecking();` e, na mesma classe:

```csharp
	/// <summary>Registra a verificação de versão de sessão (ADR-0032) — compartilhada por token e cookie.</summary>
	/// <param name="services">Coleção de serviços.</param>
	internal static IServiceCollection AddSeccoSessionVersionChecking(this IServiceCollection services)
	{
		services.AddOptions<SeccoSessionVersionOptions>()
			.BindConfiguration(SeccoAuthenticationOptions.SectionKey)
			.Validate(options => options.SessionVersionCacheTtlSeconds > 0,
				$"'{SeccoAuthenticationOptions.SectionKey}:SessionVersionCacheTtlSeconds' deve ser maior que zero.")
			.ValidateOnStart();
		services.TryAddSingleton<SessionVersionChecker>();

		return services;
	}
```

`ConfigureSeccoJwtBearerOptions.Configure(string? name, JwtBearerOptions options)` — logo depois de `options.MapInboundClaims = false;`:

```csharp
		// ADR-0032: token de sessão revogada é recusado mesmo com assinatura válida
		options.Events ??= new JwtBearerEvents();
		var previous = options.Events.OnTokenValidated;
		options.Events.OnTokenValidated = async context =>
		{
			await previous(context).ConfigureAwait(false);

			if (context.Result is not null || context.Principal is null)
			{
				return;
			}

			var checker = context.HttpContext.RequestServices.GetRequiredService<SessionVersionChecker>();

			if (!await checker.IsCurrentAsync(context.Principal, context.HttpContext.RequestAborted).ConfigureAwait(false))
			{
				context.Fail("Sessão revogada.");
			}
		};
```

(`using Microsoft.Extensions.DependencyInjection;`.)

- [ ] **Step 4: Rodar e ver passar**

Comando do Step 2 → PASS (7). Depois `dotnet test tests/SDK/Secco.SDK.AspNetCore.Tests/Secco.SDK.AspNetCore.Tests.csproj -c Release` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SDK/Secco.SDK.AspNetCore tests/SDK/Secco.SDK.AspNetCore.Tests
git commit -m "feat(sdk): produtos recusam token de sessão revogada, com cache fail-closed (ADR-0032)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Validação de sessão para aplicações de cookie (SDK)

**Files:**
- Create: `src/SDK/Secco.SDK.AspNetCore/Extensions/SeccoCookieSessionValidationExtensions.cs`
- Test: `tests/SDK/Secco.SDK.AspNetCore.Tests/Authentication/CookieSessionValidationTests.cs`

**Interfaces:**
- Consumes: `SessionVersionChecker`, `ISessionVersionResolver`, `AddSeccoSessionVersionChecking` (Task 5).
- Produces: `SeccoCookieSessionValidationExtensions.AddSeccoCookieSessionValidation(this IServiceCollection services, string cookieScheme) : IServiceCollection`.

- [ ] **Step 1: Testes que falham**

`tests/SDK/Secco.SDK.AspNetCore.Tests/Authentication/CookieSessionValidationTests.cs`:

```csharp
using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Secco.SDK.AspNetCore.Authentication;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Authentication;

/// <summary>A sessão local de uma aplicação de cookie cai quando a sessão é revogada (ADR-0032).</summary>
public class CookieSessionValidationTests
{
	private static async Task<IHost> StartAsync(ISessionVersionResolver? resolver)
	{
		return await new HostBuilder()
			.ConfigureWebHost(web =>
			{
				web.UseTestServer();
				web.ConfigureServices(services =>
				{
					services.AddRouting();
					services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
					services.AddAuthorization();

					if (resolver is not null)
					{
						services.AddSingleton(resolver);
					}

					services.AddSeccoCookieSessionValidation(CookieAuthenticationDefaults.AuthenticationScheme);
				});
				web.Configure(app =>
				{
					app.UseRouting();
					app.UseAuthentication();
					app.UseAuthorization();
					app.UseEndpoints(endpoints =>
					{
						endpoints.MapGet("/entrar", async (HttpContext context) =>
						{
							var identity = new ClaimsIdentity(
								[new Claim(SeccoClaims.Subject, "user-1"), new Claim(SeccoClaims.SessionVersion, "v1")],
								CookieAuthenticationDefaults.AuthenticationScheme);
							await context.SignInAsync(new ClaimsPrincipal(identity));
						});
						endpoints.MapGet("/perfil", (ClaimsPrincipal user) => user.Identity!.IsAuthenticated ? "logado" : "anonimo");
					});
				});
			})
			.StartAsync();
	}

	private static async Task<string> SignInAndReadProfileAsync(IHost host)
	{
		var client = host.GetTestClient();
		var signIn = await client.GetAsync("/entrar");
		var cookie = signIn.Headers.GetValues("Set-Cookie").First().Split(';')[0];

		using var request = new HttpRequestMessage(HttpMethod.Get, "/perfil");
		request.Headers.Add("Cookie", cookie);

		return await (await client.SendAsync(request)).Content.ReadAsStringAsync();
	}

	[Fact]
	public async Task VersaoIgual_ContinuaLogado()
	{
		using var host = await StartAsync(new SessionVersionValidationTests.FakeResolver());

		(await SignInAndReadProfileAsync(host)).Should().Be("logado");
	}

	[Fact]
	public async Task VersaoDiferente_CookieRejeitado()
	{
		using var host = await StartAsync(new SessionVersionValidationTests.FakeResolver { Status = new("v2", false) });

		(await SignInAndReadProfileAsync(host)).Should().Be("anonimo");
	}

	[Fact]
	public async Task Revogado_CookieRejeitado()
	{
		using var host = await StartAsync(new SessionVersionValidationTests.FakeResolver { Status = new(null, true) });

		(await SignInAndReadProfileAsync(host)).Should().Be("anonimo");
	}

	[Fact]
	public async Task ResolverFalha_CookieRejeitado()
	{
		using var host = await StartAsync(new SessionVersionValidationTests.FakeResolver { Throw = true });

		(await SignInAndReadProfileAsync(host)).Should().Be("anonimo");
	}

	[Fact]
	public async Task SemResolverRegistrado_StartupFalha()
	{
		var start = () => StartAsync(resolver: null);

		await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ISessionVersionResolver*");
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SDK/Secco.SDK.AspNetCore.Tests/Secco.SDK.AspNetCore.Tests.csproj -c Release --filter "FullyQualifiedName~CookieSessionValidationTests"`
Expected: erro de compilação.

- [ ] **Step 3: Implementar**

`Extensions/SeccoCookieSessionValidationExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.Authentication;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>
/// Validação de sessão para aplicações que autenticam por cookie a partir do login OIDC (ADR-0032): a
/// revogação na plataforma alcança quem já está logado na aplicação.
/// </summary>
public static class SeccoCookieSessionValidationExtensions
{
	/// <summary>
	/// Confere a <c>sver</c> do cookie a cada requisição HTTP. Exige um <see cref="ISessionVersionResolver"/>
	/// registrado — sem ele o startup falha, para a validação nunca ficar desligada sem ninguém perceber.
	/// </summary>
	/// <param name="services">Coleção de serviços.</param>
	/// <param name="cookieScheme">Esquema do cookie de sessão da aplicação.</param>
	public static IServiceCollection AddSeccoCookieSessionValidation(this IServiceCollection services, string cookieScheme)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentException.ThrowIfNullOrWhiteSpace(cookieScheme);

		services.AddSeccoSessionVersionChecking();
		services.AddSingleton<IStartupFilter, SessionResolverRequiredStartupFilter>();

		services.PostConfigure<CookieAuthenticationOptions>(cookieScheme, options =>
		{
			var previous = options.Events.OnValidatePrincipal;
			options.Events.OnValidatePrincipal = async context =>
			{
				await previous(context).ConfigureAwait(false);

				if (context.Principal is null)
				{
					return;
				}

				var checker = context.HttpContext.RequestServices.GetRequiredService<SessionVersionChecker>();

				if (!await checker.IsCurrentAsync(context.Principal, context.HttpContext.RequestAborted).ConfigureAwait(false))
				{
					context.RejectPrincipal();
					await context.HttpContext.SignOutAsync(cookieScheme).ConfigureAwait(false);
				}
			};
		});

		return services;
	}

	private sealed class SessionResolverRequiredStartupFilter(IServiceProvider serviceProvider) : IStartupFilter
	{
		public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
		{
			if (serviceProvider.GetService<ISessionVersionResolver>() is null)
			{
				throw new InvalidOperationException(
					"AddSeccoCookieSessionValidation exige um ISessionVersionResolver registrado " +
					"(ex.: AddSecureGateSessionVersionResolver do Secco.SecureGate.Client).");
			}

			return next;
		}
	}
}
```

- [ ] **Step 4: Rodar e ver passar** — comando do Step 2 → PASS (5).

- [ ] **Step 5: Commit**

```bash
git add src/SDK/Secco.SDK.AspNetCore tests/SDK/Secco.SDK.AspNetCore.Tests
git commit -m "feat(sdk): validação de sessão para aplicações de cookie (ADR-0032)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Resolvedor remoto no `Secco.SecureGate.Client` e prova ponta a ponta

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Client/Authorization/SecureGateSessionVersionResolver.cs`
- Create: `src/SecureGate/Secco.SecureGate.Client/Authorization/SecureGateSessionVersionResolverExtensions.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Client/Authorization/SecureGatePermissionResolverExtensions.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/CrossProductSessionRevocationTests.cs`

**Interfaces:**
- Consumes: `ISessionVersionResolver`, `SessionVersionStatus` (Task 5); rota `GetSessionVersion` → método gerado `SecureGateClient.GetSessionVersionAsync(string sub, CancellationToken)` retornando `SessionVersionDto` (Task 2); `ISessionRevoker` (Task 3).
- Produces: `SecureGateSessionVersionResolver(IHttpClientFactory httpClientFactory, SecureGateClientCredentialsOptions options)` com `HttpClientName = "Secco.SecureGate.SessionVersion"`; `AddSecureGateSessionVersionResolver(this IServiceCollection)` e `AddSecureGateSessionVersionResolver(this IServiceCollection, Func<IServiceProvider, SecureGateClientCredentialsOptions> optionsFactory)`.

- [ ] **Step 1: Teste ponta a ponta que falha**

Ler `tests/SecureGate/Secco.SecureGate.Tests/Integration/CrossProductTokenFlowTests.cs` inteiro antes: o host do LogStream abaixo copia dele a configuração de tenancy, o backchannel do JwtBearer e a migração do banco do LogStream.

`tests/SecureGate/Secco.SecureGate.Tests/Integration/CrossProductSessionRevocationTests.cs`:

```csharp
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
		// Migração do banco do tenant no LogStream: mesma chamada usada em CrossProductTokenFlowTests.InitializeAsync
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
```

Substituir o comentário `// Migração do banco do tenant no LogStream: ...` pela mesma instrução de migração que `CrossProductTokenFlowTests.InitializeAsync` usa para `secco_logstream_e2e` (trocando o nome do banco para `secco_logstream_sessoes_e2e`). Se a rota de leitura do LogStream exigir query obrigatória, usar a mesma URL de leitura que `CrossProductTokenFlowTests` usa.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~CrossProductSessionRevocationTests"`
Expected: erro de compilação (`AddSecureGateSessionVersionResolver` não existe).

- [ ] **Step 3: Implementar**

`Authorization/SecureGateSessionVersionResolver.cs`:

```csharp
using Secco.SDK.AspNetCore.Authentication;
using Secco.SecureGate.Client.Catalog;

namespace Secco.SecureGate.Client.Authorization;

/// <summary>Versão de sessão consultada no SecureGate (ADR-0032), via client credentials com <c>authorization:read</c>.</summary>
public sealed class SecureGateSessionVersionResolver(
	IHttpClientFactory httpClientFactory,
	SecureGateClientCredentialsOptions options) : ISessionVersionResolver
{
	/// <summary>Nome do <see cref="HttpClient"/> nomeado.</summary>
	public const string HttpClientName = "Secco.SecureGate.SessionVersion";

	/// <inheritdoc />
	public bool IsEnabled => options.IsConfigured;

	/// <inheritdoc />
	public async ValueTask<SessionVersionStatus> ResolveAsync(string subject, CancellationToken cancellationToken = default)
	{
		var client = new SecureGateClient(httpClientFactory.CreateClient(HttpClientName));
		var result = await client.GetSessionVersionAsync(subject, cancellationToken).ConfigureAwait(false);

		return new SessionVersionStatus(result.SessionVersion, result.Revoked);
	}
}
```

`Authorization/SecureGateSessionVersionResolverExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.SDK.AspNetCore.Authentication;
using Secco.SDK.ClientCredentials;
using Secco.SecureGate.Client.Catalog;

namespace Secco.SecureGate.Client.Authorization;

/// <summary>Registro do resolvedor remoto de versão de sessão (ADR-0032).</summary>
public static class SecureGateSessionVersionResolverExtensions
{
	/// <summary>Usa a seção <c>Secco:SecureGate</c> — a mesma do catálogo e das permissões.</summary>
	/// <param name="services">Coleção de serviços.</param>
	public static IServiceCollection AddSecureGateSessionVersionResolver(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddSecureGateClientCredentialsOptions();

		return services.AddSecureGateSessionVersionResolver(
			serviceProvider => serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>());
	}

	/// <summary>
	/// Usa credenciais próprias — para aplicação cujo client da seção <c>Secco:SecureGate</c> não deve ganhar
	/// client credentials (ex.: o AdminPortal, cujo client pode pedir <c>securegate:admin</c>).
	/// </summary>
	/// <param name="services">Coleção de serviços.</param>
	/// <param name="optionsFactory">Credenciais com acesso a <c>authorization:read</c>.</param>
	public static IServiceCollection AddSecureGateSessionVersionResolver(
		this IServiceCollection services,
		Func<IServiceProvider, SecureGateClientCredentialsOptions> optionsFactory)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(optionsFactory);

		// Store próprio: o token desta consulta não se mistura com o do catálogo nem com o de permissões
		var tokenStore = new SeccoAccessTokenStore();

		services.AddHttpClient(SecureGateSessionVersionResolver.HttpClientName)
			.ConfigureHttpClient((serviceProvider, client) =>
			{
				var options = optionsFactory(serviceProvider);

				if (options.IsConfigured)
				{
					client.BaseAddress = new Uri(options.BaseUrl!, UriKind.Absolute);
				}
			})
			.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
			{
				var options = optionsFactory(serviceProvider);

				if (options.IsConfigured)
				{
					handlers.Add(new SeccoClientCredentialsHandler(
						options.BaseUrl!, options.ClientId!, options.ClientSecret!,
						SecureGateClientCredentialsOptions.AuthorizationScope, tokenStore));
				}
			});

		services.TryAddSingleton<ISessionVersionResolver>(serviceProvider =>
		{
			var options = optionsFactory(serviceProvider);

			if (options.IsConfigured)
			{
				options.Validate(requireProduct: false);
			}

			return new SecureGateSessionVersionResolver(serviceProvider.GetRequiredService<IHttpClientFactory>(), options);
		});

		return services;
	}
}
```

(Se `AddSecureGateClientCredentialsOptions` ou `SecureGateClientCredentialsOptions.AuthorizationScope` forem `internal` e o compilador acusar, eles estão no mesmo assembly — não há ajuste; se `Validate` tiver outra assinatura, usar a mesma chamada de `SecureGatePermissionResolverExtensions`.)

`SecureGatePermissionResolverExtensions.AddSecureGatePermissionResolver` — antes de `return services;`:

```csharp
		// ADR-0032: quem resolve permissões no SecureGate também confere a versão de sessão — nenhum produto
		// fica sem a verificação por esquecer uma linha
		services.AddSecureGateSessionVersionResolver();
```

- [ ] **Step 4: Rodar e ver passar**

Comando do Step 2 → PASS. Depois `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release` e `dotnet test tests/LogStream/Secco.LogStream.Tests/Secco.LogStream.Tests.csproj -c Release` → PASS (LogStream em DEV/Testing sem `Secco:SecureGate` segue com resolvedor desabilitado).

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Client tests/SecureGate
git commit -m "feat(securegate-client): resolvedor remoto de versão de sessão e prova entre produtos (ADR-0032)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: AdminPortal — cofre de tokens, renovação e validação de sessão

**Files:**
- Create: `src/AdminPortal/Secco.AdminPortal/Authentication/OperatorSession.cs`
- Create: `src/AdminPortal/Secco.AdminPortal/Authentication/IOperatorSessionStore.cs`
- Create: `src/AdminPortal/Secco.AdminPortal/Authentication/OperatorTokenRefresher.cs`
- Modify: `src/AdminPortal/Secco.AdminPortal/Authentication/OperatorTokenProvider.cs`
- Modify: `src/AdminPortal/Secco.AdminPortal/Authentication/AdminPortalDefaults.cs`
- Modify: `src/AdminPortal/Secco.AdminPortal/Authentication/AdminPortalAuthenticationExtensions.cs`
- Modify: `src/AdminPortal/Secco.AdminPortal/appsettings.Development.json`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Seeding/SecureGateDevelopmentDataSeeder.cs`
- Test: `tests/AdminPortal/Secco.AdminPortal.Tests/OperatorTokenProviderTests.cs` (reescrito)

**Interfaces:**
- Consumes: `AddSeccoCookieSessionValidation` (Task 6); `AddSecureGateSessionVersionResolver(Func<...>)` (Task 7).
- Produces: `OperatorSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt)`; `IOperatorSessionStore` (`GetAsync(string id, CancellationToken)`, `SetAsync(string id, OperatorSession session, CancellationToken)`, `RemoveAsync(string id, CancellationToken)`); `IOperatorTokenRefresher.RefreshAsync(string refreshToken, CancellationToken) : Task<OperatorSession?>`; `AdminPortalDefaults.SessionIdClaim = "admin_session"`.

- [ ] **Step 1: Testes que falham**

Reescrever `tests/AdminPortal/Secco.AdminPortal.Tests/OperatorTokenProviderTests.cs`:

```csharp
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using NSubstitute;
using Secco.AdminPortal.Authentication;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// O access token do operador vive num cofre no servidor, e o cookie só leva o id da sessão (ADR-0032). O
/// provider renova perto do vencimento, uma vez por sessão, e manda para novo login quando não dá.
/// </summary>
public class OperatorTokenProviderTests
{
	private const string SessionId = "sessao-1";

	private sealed class StubAuthStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
	{
		public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
			Task.FromResult(new AuthenticationState(user));
	}

	private sealed class RecordingNavigation : NavigationManager
	{
		public string? ForcedTo { get; private set; }

		public RecordingNavigation() => Initialize("https://portal.test/", "https://portal.test/tenants");

		protected override void NavigateToCore(string uri, NavigationOptions options) =>
			ForcedTo = options.ForceLoad ? uri : null;
	}

	private static ClaimsPrincipal Principal() =>
		new(new ClaimsIdentity([new Claim(AdminPortalDefaults.SessionIdClaim, SessionId)], "test"));

	private static (OperatorTokenProvider Provider, IOperatorSessionStore Store, IOperatorTokenRefresher Refresher, RecordingNavigation Navigation)
		Build(OperatorSession? stored)
	{
		var store = Substitute.For<IOperatorSessionStore>();
		store.GetAsync(SessionId, Arg.Any<CancellationToken>()).Returns(stored);
		var refresher = Substitute.For<IOperatorTokenRefresher>();
		var navigation = new RecordingNavigation();

		return (new OperatorTokenProvider(new StubAuthStateProvider(Principal()), store, refresher, navigation), store, refresher, navigation);
	}

	[Fact]
	public async Task TokenValido_DevolveSemRenovar()
	{
		var (provider, _, refresher, _) = Build(new OperatorSession("a1", "r1", DateTimeOffset.UtcNow.AddMinutes(4)));

		(await provider.GetAccessTokenAsync()).Should().Be("a1");
		await refresher.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
	}

	[Fact]
	public async Task PertoDoVencimento_RenovaEGrava()
	{
		var (provider, store, refresher, _) = Build(new OperatorSession("a1", "r1", DateTimeOffset.UtcNow.AddSeconds(30)));
		var renewed = new OperatorSession("a2", "r2", DateTimeOffset.UtcNow.AddMinutes(5));
		refresher.RefreshAsync("r1", Arg.Any<CancellationToken>()).Returns(renewed);

		(await provider.GetAccessTokenAsync()).Should().Be("a2");
		await store.Received(1).SetAsync(SessionId, renewed, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ChamadasSimultaneas_UmaRenovacao()
	{
		var session = new OperatorSession("a1", "r1", DateTimeOffset.UtcNow.AddSeconds(30));
		var renewed = new OperatorSession("a2", "r2", DateTimeOffset.UtcNow.AddMinutes(5));
		var store = Substitute.For<IOperatorSessionStore>();
		var current = session;
		store.GetAsync(SessionId, Arg.Any<CancellationToken>()).Returns(_ => current);
		store.When(s => s.SetAsync(SessionId, Arg.Any<OperatorSession>(), Arg.Any<CancellationToken>()))
			.Do(call => current = call.ArgAt<OperatorSession>(1));
		var refresher = Substitute.For<IOperatorTokenRefresher>();
		refresher.RefreshAsync("r1", Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			await Task.Delay(50);
			return renewed;
		});
		var provider = new OperatorTokenProvider(new StubAuthStateProvider(Principal()), store, refresher, new RecordingNavigation());

		var tokens = await Task.WhenAll(provider.GetAccessTokenAsync(), provider.GetAccessTokenAsync());

		tokens.Should().OnlyContain(token => token == "a2");
		await refresher.Received(1).RefreshAsync("r1", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task RenovacaoRecusada_RemoveSessaoEForcaNovoLogin()
	{
		var (provider, store, refresher, navigation) = Build(new OperatorSession("a1", "r1", DateTimeOffset.UtcNow.AddSeconds(30)));
		refresher.RefreshAsync("r1", Arg.Any<CancellationToken>()).Returns((OperatorSession?)null);

		(await provider.GetAccessTokenAsync()).Should().BeNull();
		await store.Received(1).RemoveAsync(SessionId, Arg.Any<CancellationToken>());
		navigation.ForcedTo.Should().Be("https://portal.test/tenants");
	}

	[Fact]
	public async Task SessaoAusenteNoCofre_ForcaNovoLogin()
	{
		var (provider, _, _, navigation) = Build(stored: null);

		(await provider.GetAccessTokenAsync()).Should().BeNull();
		navigation.ForcedTo.Should().NotBeNull();
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/AdminPortal/Secco.AdminPortal.Tests/Secco.AdminPortal.Tests.csproj -c Release --filter "FullyQualifiedName~OperatorTokenProviderTests"`
Expected: erro de compilação.

- [ ] **Step 3: Implementar**

`Authentication/OperatorSession.cs`:

```csharp
namespace Secco.AdminPortal.Authentication;

/// <summary>Tokens do operador guardados no servidor — nunca no cookie (ADR-0032).</summary>
/// <param name="AccessToken">Access token atual.</param>
/// <param name="RefreshToken">Refresh token atual (rotativo).</param>
/// <param name="ExpiresAt">Vencimento do access token.</param>
public sealed record OperatorSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
```

`Authentication/IOperatorSessionStore.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Secco.AdminPortal.Authentication;

/// <summary>Cofre de sessões do operador, na chave de um id aleatório guardado no cookie.</summary>
public interface IOperatorSessionStore
{
	/// <summary>Sessão, ou <c>null</c> se expirou ou o AdminPortal reiniciou.</summary>
	Task<OperatorSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

	/// <summary>Grava ou substitui a sessão.</summary>
	Task SetAsync(string sessionId, OperatorSession session, CancellationToken cancellationToken = default);

	/// <summary>Descarta a sessão.</summary>
	Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cofre sobre <see cref="IDistributedCache"/> — em memória por padrão; mais de uma instância do AdminPortal
/// exige cache distribuído (ADR-0032).
/// </summary>
internal sealed class DistributedOperatorSessionStore(IDistributedCache cache) : IOperatorSessionStore
{
	private static readonly DistributedCacheEntryOptions Entry = new() { SlidingExpiration = TimeSpan.FromHours(8) };

	private static string Key(string sessionId) => "adminportal:session:" + sessionId;

	public async Task<OperatorSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default) =>
		await cache.GetAsync(Key(sessionId), cancellationToken).ConfigureAwait(false) is { } bytes
			? JsonSerializer.Deserialize<OperatorSession>(bytes)
			: null;

	public Task SetAsync(string sessionId, OperatorSession session, CancellationToken cancellationToken = default) =>
		cache.SetAsync(Key(sessionId), JsonSerializer.SerializeToUtf8Bytes(session), Entry, cancellationToken);

	public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default) =>
		cache.RemoveAsync(Key(sessionId), cancellationToken);
}
```

`Authentication/OperatorTokenRefresher.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace Secco.AdminPortal.Authentication;

/// <summary>Renova os tokens do operador no token endpoint do SecureGate.</summary>
public interface IOperatorTokenRefresher
{
	/// <summary>Novo par de tokens, ou <c>null</c> se a renovação foi recusada (sessão revogada).</summary>
	Task<OperatorSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>Grant <c>refresh_token</c> como client confidencial.</summary>
internal sealed class OperatorTokenRefresher(IHttpClientFactory httpClientFactory, IConfiguration configuration)
	: IOperatorTokenRefresher
{
	public async Task<OperatorSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
	{
		var client = httpClientFactory.CreateClient(AdminPortalDefaults.SecureGateHttpClient);
		var authority = configuration["Secco:SecureGate:Authority"]!.TrimEnd('/');

		using var response = await client.PostAsync(authority + "/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["refresh_token"] = refreshToken,
			["client_id"] = configuration["Secco:SecureGate:ClientId"]!,
			["client_secret"] = configuration["Secco:SecureGate:ClientSecret"]!,
		}), cancellationToken).ConfigureAwait(false);

		if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
		{
			return null;
		}

		response.EnsureSuccessStatusCode();

		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
		var root = json.RootElement;

		return new OperatorSession(
			root.GetProperty("access_token").GetString()!,
			root.GetProperty("refresh_token").GetString()!,
			DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
	}
}
```

`Authentication/OperatorTokenProvider.cs` (substituir):

```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Secco.AdminPortal.Authentication;

/// <summary>
/// Access token do operador a partir do cofre (ADR-0032): renova quando faltam menos de 60 s, uma renovação por
/// sessão — o refresh token é rotativo, e duas renovações simultâneas derrubariam a segunda.
/// </summary>
internal sealed class OperatorTokenProvider(
	AuthenticationStateProvider authenticationStateProvider,
	IOperatorSessionStore store,
	IOperatorTokenRefresher refresher,
	NavigationManager navigation) : IOperatorTokenProvider
{
	private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

	private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

	public async Task<string?> GetAccessTokenAsync()
	{
		var state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);

		if (state.User.FindFirst(AdminPortalDefaults.SessionIdClaim)?.Value is not { Length: > 0 } sessionId)
		{
			return null;
		}

		var session = await store.GetAsync(sessionId).ConfigureAwait(false);

		if (session is null)
		{
			return Reauthenticate();
		}

		if (session.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
		{
			return session.AccessToken;
		}

		var gate = Locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync().ConfigureAwait(false);

		try
		{
			// Outra chamada pode ter renovado enquanto esta esperava
			session = await store.GetAsync(sessionId).ConfigureAwait(false);

			if (session is null)
			{
				return Reauthenticate();
			}

			if (session.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
			{
				return session.AccessToken;
			}

			var renewed = await refresher.RefreshAsync(session.RefreshToken).ConfigureAwait(false);

			if (renewed is null)
			{
				await store.RemoveAsync(sessionId).ConfigureAwait(false);
				return Reauthenticate();
			}

			await store.SetAsync(sessionId, renewed).ConfigureAwait(false);
			return renewed.AccessToken;
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	/// Recarga completa da página: a requisição HTTP passa pela validação do cookie, que rejeita a sessão sem
	/// cofre e leva ao login.
	/// </summary>
	private string? Reauthenticate()
	{
		navigation.NavigateTo(navigation.Uri, forceLoad: true);
		return null;
	}
}
```

`AdminPortalDefaults.cs`: remover `AccessTokenClaim` e acrescentar:

```csharp
	/// <summary>Claim do cookie com o id da sessão no cofre (ADR-0032) — o token nunca vai ao cookie.</summary>
	public const string SessionIdClaim = "admin_session";

	/// <summary>Seção das credenciais próprias da consulta de versão de sessão.</summary>
	public const string SessionValidationSection = "Secco:SessionValidation";
```

`AdminPortalAuthenticationExtensions.cs`:

1. No `.AddCookie(options => { ... })`, acrescentar ao fim:

```csharp
				// Sessão sem cofre (expirou ou o AdminPortal reiniciou) não vale: volta ao login
				options.Events.OnValidatePrincipal = async context =>
				{
					var store = context.HttpContext.RequestServices.GetRequiredService<IOperatorSessionStore>();

					if (context.Principal?.FindFirst(AdminPortalDefaults.SessionIdClaim)?.Value is not { Length: > 0 } sessionId
						|| await store.GetAsync(sessionId, context.HttpContext.RequestAborted) is null)
					{
						context.RejectPrincipal();
						await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
					}
				};
```

2. Trocar o `OnTokenValidated` inteiro por:

```csharp
					OnTokenValidated = async context =>
					{
						if (context.TokenEndpointResponse is not { AccessToken.Length: > 0, RefreshToken.Length: > 0 } tokens
							|| context.Principal?.Identity is not ClaimsIdentity identity)
						{
							context.Fail("O SecureGate não devolveu access e refresh token.");
							return;
						}

						var sessionId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
						var expiresIn = int.TryParse(tokens.ExpiresIn, out var seconds) ? seconds : 300;

						await context.HttpContext.RequestServices.GetRequiredService<IOperatorSessionStore>().SetAsync(
							sessionId,
							new OperatorSession(tokens.AccessToken, tokens.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn)),
							context.HttpContext.RequestAborted);

						identity.AddClaim(new Claim(AdminPortalDefaults.SessionIdClaim, sessionId));
					},
```

(usings: `System.Security.Cryptography`, `Microsoft.AspNetCore.Authentication`, `Microsoft.IdentityModel.Tokens`, `Secco.SDK.AspNetCore.Extensions`, `Secco.SecureGate.Client.Authorization`, `Secco.SecureGate.Client.Catalog`.)

3. Antes de `services.AddScoped<IOperatorTokenProvider, OperatorTokenProvider>();`:

```csharp
		services.AddDistributedMemoryCache();
		services.AddSingleton<IOperatorSessionStore, DistributedOperatorSessionStore>();
		services.AddScoped<IOperatorTokenRefresher, OperatorTokenRefresher>();

		// ADR-0032: revogar na plataforma derruba o cookie do operador. Credenciais PRÓPRIAS: o client
		// secco-adminportal pode pedir securegate:admin e não pode ganhar client credentials.
		services.AddSecureGateSessionVersionResolver(_ =>
			configuration.GetSection(AdminPortalDefaults.SessionValidationSection).Get<SecureGateClientCredentialsOptions>()
			?? new SecureGateClientCredentialsOptions());
		services.AddSeccoCookieSessionValidation(CookieAuthenticationDefaults.AuthenticationScheme);
```

`appsettings.Development.json`, dentro de `Secco`:

```json
    "SessionValidation": {
      "BaseUrl": "https://localhost:4001",
      "ClientId": "secco-adminportal-sessions",
      "ClientSecret": "secco-adminportal-sessions-secret-32-chars!"
    },
```

`SecureGateDevelopmentDataSeeder.cs` — constantes e client novos (seguir o padrão de `SeedAdminPortalClientAsync`, com `UpdateAsync` se existir):

```csharp
	/// <summary>Client de máquina do AdminPortal só para consultar versão de sessão (ADR-0032).</summary>
	public const string AdminPortalSessionsClientId = "secco-adminportal-sessions";

	/// <summary>Secret do client de sessões do AdminPortal (conhecido — só existe em DEV).</summary>
	public const string AdminPortalSessionsClientSecret = "secco-adminportal-sessions-secret-32-chars!";

	private async Task SeedAdminPortalSessionsClientAsync(CancellationToken cancellationToken)
	{
		var descriptor = new OpenIddictApplicationDescriptor
		{
			ClientId = AdminPortalSessionsClientId,
			ClientSecret = AdminPortalSessionsClientSecret,
			ClientType = ClientTypes.Confidential,
			DisplayName = "Secco AdminPortal — versão de sessão",
			Permissions =
			{
				Permissions.Endpoints.Token,
				Permissions.GrantTypes.ClientCredentials,
				// Só leitura de autorização: este client nunca pode pedir securegate:admin
				Permissions.Prefixes.Scope + Application.SecureGateScopes.AuthorizationRead,
			},
		};

		if (await applicationManager.FindByClientIdAsync(AdminPortalSessionsClientId, cancellationToken).ConfigureAwait(false) is { } existing)
		{
			await applicationManager.UpdateAsync(existing, descriptor, cancellationToken).ConfigureAwait(false);
			return;
		}

		await applicationManager.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
	}
```

e chamar `await SeedAdminPortalSessionsClientAsync(cancellationToken).ConfigureAwait(false);` em `SeedAsync`, logo depois da chamada de `SeedAdminPortalClientAsync`.

Remover usos restantes de `AdminPortalDefaults.AccessTokenClaim` (`grep -rn AccessTokenClaim src tests` deve voltar vazio).

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build src/AdminPortal/Secco.AdminPortal/Secco.AdminPortal.csproj -c Release` → 0 avisos. `dotnet test tests/AdminPortal/Secco.AdminPortal.Tests/Secco.AdminPortal.Tests.csproj -c Release` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdminPortal src/SecureGate/Secco.SecureGate.Infrastructure/Seeding tests/AdminPortal
git commit -m "feat(adminportal): tokens do operador num cofre no servidor, com renovação e validação de sessão (ADR-0032)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Mutação, suíte completa e documentação

**Files:**
- Modify: `CHANGELOG.md`, `docs/roadmap.md`, `CLAUDE.md`

- [ ] **Step 1: Mutação**

Para cada linha: aplicar, rodar o filtro indicado, confirmar que o teste esperado falha **e que o build compilou** (a saída não pode conter `: error `), restaurar com `git checkout -- <arquivo>`.

| # | Arquivo | Mutação | Filtro / teste que deve falhar |
| --- | --- | --- | --- |
| M1 | `SessionRevoker.cs` | remover a chamada `UpdateSecurityStampAsync` e o `if (!stamped.Succeeded)` (declarar `var stamped = IdentityResult.Success;`) | `SessionRevocationEffectTests.RevokeUserSessions_TrocaAVersao` |
| M2 | `SessionRevoker.cs` | remover `await tokens.RevokeBySubjectAsync(...)` | `SessionRevocationTests.EncerrarSessoes_RefreshRecusadoNaHora` |
| M3 | `InteractiveEndpoints.cs` | `var user = await signInManager.ValidateSecurityStampAsync(authentication.Principal);` → `var user = await userManager.GetUserAsync(authentication.Principal!);` | `Cookie_DepoisDeEncerrarSessoes_NaoEmiteNovoCode` |
| M4 | `SessionVersionChecker.cs` | `return false;` do `catch` → `return true;` | `SessionVersionValidationTests.ResolverFalha_FailClosed401` |
| M5 | `SessionVersionChecker.cs` | `return !entry.Status.Revoked && ...` → `return string.Equals(entry.Status.SessionVersion, presented, StringComparison.Ordinal) \|\| entry.Status.Revoked;` | `Revogado_401` |
| M6 | `OidcPrincipalBuilder.cs` (`ForElevation`) | remover a linha da `sver` | `Exchange_TokenElevadoLevaVersaoDeSessao` |
| M7 | `GetSessionVersionHandler.cs` | `return tenant is { IsActive: true } ? ... : RevokedState;` → sempre o primeiro ramo | `Versao_TenantInativo_Revogado` |
| M8 | `RemoveUserRoleHandler.cs` | `if (outcome == RoleAssignmentOutcome.Done)` → `if (outcome is RoleAssignmentOutcome.Done or RoleAssignmentOutcome.NotAssigned)` | `RemoverPerfil_Efetivo_TrocaAVersao_Repetido_NaoTroca` |
| M9 | `SetUserActivationHandler.cs` | remover o bloco `if (!command.Active) { await revoker... }` | `DesativarEReativar_RefreshAnteriorContinuaRecusado` |
| M10 | `SeccoCookieSessionValidationExtensions.cs` | remover `context.RejectPrincipal();` | `CookieSessionValidationTests.Revogado_CookieRejeitado` |
| M11 | `OperatorTokenProvider.cs` | remover o segundo `session = await store.GetAsync(...)` e o `if` seguinte dentro do lock | `ChamadasSimultaneas_UmaRenovacao` |

Registrar o placar para o roadmap.

- [ ] **Step 2: Suíte completa**

Run: `dotnet build Secco.Platform.slnx --configuration Release` → 0 erros, nenhum aviso novo em arquivo criado ou alterado neste plano.
Run: `dotnet test Secco.Platform.slnx -c Release --no-build` → tudo PASS; registrar totais.

- [ ] **Step 3: Documentação**

`CHANGELOG.md`, seção `## Não publicado` (substituir `_Nada pendente._ ...`):

```markdown
### Secco.SharedKernel

- **Adicionado** `SeccoClaims.SessionVersion` (`sver`) — versão de sessão do token (ADR-0032).

### Secco.SDK.AspNetCore

- **Adicionado** verificação de versão de sessão em `AddSeccoAuthentication()`: token com `sver` divergente ou revogada responde **401**, com cache por usuário (`Secco:Authentication:SessionVersionCacheTtlSeconds`, padrão 60) e **fail-closed**. Token sem `sver` passa; sem `ISessionVersionResolver` habilitado (DEV standalone), não verifica.
- **Adicionado** `AddSeccoCookieSessionValidation(cookieScheme)` para aplicações de cookie: revogar na plataforma derruba a sessão local. Exige `ISessionVersionResolver` registrado — sem ele, o startup falha.

### Secco.SecureGate.Client

- **Adicionado** `SecureGateSessionVersionResolver` e `AddSecureGateSessionVersionResolver()` (também com credenciais próprias); `AddSecureGatePermissionResolver()` passa a registrá-lo — produtos que já resolvem permissões ganham a verificação só atualizando o pacote.
- **Adicionado (aditivo)** `GetSessionVersionAsync` e `RevokeUserSessionsAsync`.
- **Mudança de comportamento do servidor que o adotante percebe:**
  - access token padrão de **5 minutos** (era 60). Cliente que não renova token perde acesso a cada 5 minutos; `SecureGate:Tokens:AccessTokenLifetimeMinutes` volta ao valor antigo;
  - desativar usuário e remover perfil **revogam** as sessões na hora (antes, só a próxima renovação era recusada);
  - `/connect/authorize` passa a exigir cookie com security stamp atual: após qualquer revogação, o navegador volta ao login.
- **Adoção no secco-intranet:** atualizar os pacotes e chamar `AddSecureGateSessionVersionResolver()` + `AddSeccoCookieSessionValidation(<esquema do cookie>)` — sem isso, um usuário revogado segue logado na Intranet até o cookie dela expirar. A Intranet não guarda token de usuário, então o access token de 5 minutos não a afeta.
```

`docs/roadmap.md` — antes de `## Backlog`:

```markdown
- [x] **Sessões e revogação efetiva** (**ADR-0032**, 2026-09-17, spec `docs/superpowers/specs/2026-09-17-sessoes-e-revogacao-design.md`): "encerrar sessão" não encerrava — o access token JWT seguia válido até expirar, nada revogava os tokens do OpenIddict, o cookie de login do SecureGate emitia tokens novos depois de qualquer revogação (sem `SecurityStampValidator` no `AddIdentityCore`), e a sessão local das aplicações clientes nem era tocada. Agora todo token de usuário carrega `sver` (hash do `SecurityStamp`); produtos e aplicações de cookie conferem a versão com cache fail-closed; a revogação única troca o stamp antes de revogar no OpenIddict; o authorize valida o cookie; access token de 5 minutos como segunda barreira. O AdminPortal tirou o access token do cookie (cofre no servidor, renovação serializada) e consulta a versão por um client próprio só com `authorization:read` — o `secco-adminportal` pode pedir `securegate:admin`, e habilitar client credentials nele permitiria emitir token de admin sem pessoa. Mutação: <PLACAR>. Verificado: <TOTAIS>.
```

(substituir os marcadores pelos números reais.)

`CLAUDE.md`, parágrafo "Estado atual", antes de `Aberta a issue #6`:

```markdown
**Sessões e revogação** (**ADR-0032**, 2026-09-17): claim `sver` (hash do `SecurityStamp`) em todo token de usuário; produtos (JwtBearer) e aplicações de cookie conferem a versão com cache fail-closed; revogação única (stamp → OpenIddict) disparada por "encerrar sessões", desativação e remoção de perfil; authorize valida o cookie contra o stamp; access token padrão de 5 minutos; AdminPortal com tokens num cofre no servidor e renovação.
```

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md docs/roadmap.md CLAUDE.md
git commit -m "docs: sessões e revogação efetiva (ADR-0032)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Parar e perguntar**

Não empurrar. Perguntar ao usuário: verificação manual (login no AdminPortal ficando mais de 5 minutos sem cair; "Encerrar sessões" pela API derrubando o operador em outra aba), push, publicação da cadeia (`python scripts/check-release-chain.py securegate-client/v`, uma tag por vez após CI verde) e aviso ao secco-intranet.
