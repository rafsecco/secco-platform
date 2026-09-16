# Gestão de perfis de acesso — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Administrador completo de perfis (Role do Identity) no SecureGate e no AdminPortal — ver, excluir, atribuir e remover perfis de usuários existentes, com detalhe do usuário e acesso efetivo — fechando a issue #26.

**Architecture:** Perfil = `Role` nativo do ASP.NET Identity (ADR-0021 intacta): `tb_roles` + `tb_role_claims` + `tb_user_roles`, sem tabela nova nem migração. Regras em handlers da Application (Result pattern), dados no `RoleRepository`/`UserAccountService` da Infrastructure, endpoints mínimos na Api, sempre resolvendo perfil por `(tenant, nome normalizado)`. Corrige de passagem o refresh que copiava scopes do token anterior.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, ASP.NET Identity + OpenIddict 7.5, EF Core (SQL Server/PostgreSQL), NSwag, Blazor Server, xUnit + FluentAssertions 7 + NSubstitute + Testcontainers.

**Spec:** [`docs/superpowers/specs/2026-09-16-gestao-de-perfis-design.md`](../specs/2026-09-16-gestao-de-perfis-design.md)

## Global Constraints

- Nenhuma tabela nova, nenhuma migração, nenhuma ADR nova; ADR-0021/0026 intactas.
- Na API o termo é `roles`; nas telas, "Perfil".
- Proibidos no SecureGate: `AddToRoleAsync`, `AddToRolesAsync`, `RemoveFromRoleAsync`, `RemoveFromRolesAsync`, `IsInRoleAsync`, `GetUsersInRoleAsync`, `RoleManager<`.
- Perfil sempre localizado por `(tenant, NormalizedName)` com `ToUpperInvariant()` (mesmo normalizador do `RoleRepository`).
- Usuário/perfil de outro tenant responde **404 idêntico a inexistente**.
- Nome de perfil na rota validado por `RoleInputRules.IsValidName` antes de qualquer consulta.
- Perfis só de token (`installation-log-reader`, `installation-auditor`, `platform-operator`) nunca atribuíveis a usuário → **400** `SecureGate.User.RoleNotAssignable`, nos dois caminhos (criar e atribuir).
- `installation-operator` nunca excluível (409); ninguém se remove dele (409); não remover/desativar o último operador ativo (409).
- Status de usuário: strings `Active`, `Deactivated`, `LockedOut` (sem enum no contrato — o client NSwag trata enum mal, ver Fase 7.3).
- Paginação de membros com `PageRequest`/`PagedResult` do SharedKernel (tamanho máximo do SharedKernel, 200).
- Build Release com warnings = erros em código de produto; C# com **tab**; arquivos com **CRLF**.
- Testes de integração por HTTP; mudança de token provada com tokens emitidos de verdade.
- Commits Conventional Commits, direto na `main`, terminando com `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. **Não empurrar sem perguntar.**
- Comando de verificação final: `dotnet test Secco.Platform.slnx` (a solution inteira).

## Mapa de arquivos

**Application** (`src/SecureGate/Secco.SecureGate.Application/`)
- `SecureGateErrors.cs` — erros novos (modificar)
- `Roles/RoleInputRules.cs` — `IsAssignableToUsers` (modificar)
- `Roles/IRoleRepository.cs` — `FindRoleAsync`, `ListMembersAsync`, `DeleteRoleAsync` e tipos de dado (modificar)
- `Roles/RoleDetailDto.cs` — `RoleDetailDto`, `RoleMemberDto` (criar)
- `Roles/GetRoleHandler.cs`, `Roles/ListRoleMembersHandler.cs`, `Roles/DeleteRoleHandler.cs` (criar)
- `Users/UserStatuses.cs` — constantes e derivação de status (criar)
- `Users/UserDto.cs` — `Status` (modificar)
- `Users/UserDetailDto.cs` (criar)
- `Users/IUserDirectory.cs` — `GetAsync`, `AddRoleAsync`, `RemoveRoleAsync`, `HasRoleAsync`, `CountActiveOperatorsAsync` e tipos (modificar)
- `Users/GetUserHandler.cs`, `Users/AddUserRoleHandler.cs`, `Users/RemoveUserRoleHandler.cs`, `Users/OperatorGuard.cs` (criar)
- `Users/CreateUserHandler.cs`, `Users/SetUserActivationHandler.cs` (modificar)
- `SecureGateApplicationExtensions.cs` — registro dos handlers (modificar)

**Infrastructure** (`src/SecureGate/Secco.SecureGate.Infrastructure/`)
- `Roles/RoleRepository.cs`, `Users/UserAccountService.cs` (modificar)

**Api** (`src/SecureGate/Secco.SecureGate.Api/`)
- `Identity/InstallationOperatorPolicy.cs` (criar)
- `Identity/OidcPrincipalBuilder.cs`, `Endpoints/InteractiveEndpoints.cs`, `Endpoints/TokenEndpoints.cs` (modificar)
- `Endpoints/RoleEndpoints.cs`, `Endpoints/UserEndpoints.cs` (modificar)
- `openapi/openapi.json` (regenerar)

**AdminPortal** (`src/AdminPortal/Secco.AdminPortal/`)
- `Services/AdminModels.cs`, `Services/IUserAdminService.cs`, `Services/IRoleAdminService.cs` (modificar)
- `Components/Pages/TenantManagement.razor` (reescrever)
- `Components/Pages/RoleManagement.razor`, `Components/Pages/UserManagement.razor` (criar)

**Testes** (`tests/SecureGate/Secco.SecureGate.Tests/`, `tests/AdminPortal/Secco.AdminPortal.Tests/`)
- `Unit/UserStatusesTests.cs`, `Unit/RoleAssignabilityTests.cs`, `Unit/IdentityRoleApiGuardTests.cs` (criar)
- `Integration/OidcLoginDriver.cs`, `Integration/IdentitySeed.cs` (criar)
- `Integration/SessionRevocationTests.cs` (modificar: usar o driver)
- `Integration/OperatorDemotionTests.cs`, `Integration/RoleProfileManagementTests.cs`, `Integration/UserProfileManagementTests.cs`, `Integration/ProfileAssignmentTokenTests.cs`, `Integration/LastOperatorGuardTests.cs` (criar)
- `IdentityAdminServicesTests.cs` (modificar)

---

### Task 1: Regras puras — status do usuário, atribuibilidade e erros

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Users/UserStatuses.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Roles/RoleInputRules.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateErrors.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/UserStatusesTests.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/RoleAssignabilityTests.cs`

**Interfaces:**
- Produces: `UserStatuses.Active|Deactivated|LockedOut` (string), `UserStatuses.From(bool lockoutEnabled, DateTimeOffset? lockoutEnd, DateTimeOffset now) : string`; `RoleInputRules.IsAssignableToUsers(string name) : bool`; erros `SecureGateErrors.Roles.CannotDeleteReserved`, `Roles.HasMembers`, `Users.RoleNotAssignable`, `Users.CannotRemoveSelfFromOperator`, `Users.LastActiveOperator`.

- [ ] **Step 1: Escrever os testes que falham**

`tests/SecureGate/Secco.SecureGate.Tests/Unit/UserStatusesTests.cs`:

```csharp
using FluentAssertions;
using Secco.SecureGate.Application.Users;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Derivação do status exibido ao admin a partir do lockout do Identity. A desativação grava
/// <see cref="DateTimeOffset.MaxValue"/>; bloqueio por tentativas grava uma data próxima.
/// </summary>
public class UserStatusesTests
{
	private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void SemData_Ativo() =>
		UserStatuses.From(lockoutEnabled: true, lockoutEnd: null, Now).Should().Be(UserStatuses.Active);

	[Fact]
	public void DataNoPassado_Ativo() =>
		UserStatuses.From(lockoutEnabled: true, Now.AddMinutes(-1), Now).Should().Be(UserStatuses.Active);

	[Fact]
	public void LockoutDesligado_AtivoMesmoComDataFutura() =>
		UserStatuses.From(lockoutEnabled: false, DateTimeOffset.MaxValue, Now).Should().Be(UserStatuses.Active);

	[Fact]
	public void DataMaxima_Desativado() =>
		UserStatuses.From(lockoutEnabled: true, DateTimeOffset.MaxValue, Now).Should().Be(UserStatuses.Deactivated);

	[Fact]
	public void DataMaximaTruncadaPeloBanco_ContinuaDesativado()
	{
		// O PostgreSQL guarda microssegundos: o MaxValue volta com o último tick a menos
		var truncated = DateTimeOffset.MaxValue.AddTicks(-9);

		UserStatuses.From(lockoutEnabled: true, truncated, Now).Should().Be(UserStatuses.Deactivated);
	}

	[Fact]
	public void DataProximaNoFuturo_Bloqueado() =>
		UserStatuses.From(lockoutEnabled: true, Now.AddMinutes(5), Now).Should().Be(UserStatuses.LockedOut);
}
```

`tests/SecureGate/Secco.SecureGate.Tests/Unit/RoleAssignabilityTests.cs`:

```csharp
using FluentAssertions;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Roles;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Quais perfis podem ter usuários como membros. Os reservados que são identidades SÓ de token
/// (ADR-0031) nunca podem: um usuário membro deles teria o read-set cross-tenant pelo nome.
/// </summary>
public class RoleAssignabilityTests
{
	[Theory]
	[InlineData("financeiro-admin")]
	[InlineData(SecureGatePlatform.OperatorRole)]
	[InlineData("INSTALLATION-OPERATOR")]
	public void Atribuivel(string name) => RoleInputRules.IsAssignableToUsers(name).Should().BeTrue();

	[Theory]
	[InlineData(SecureGatePlatform.ElevatedLogReaderRole)]
	[InlineData(SecureGatePlatform.AuditorRole)]
	[InlineData(SecureGatePlatform.LegacyOperatorRole)]
	[InlineData("INSTALLATION-AUDITOR")]
	public void NaoAtribuivel(string name) => RoleInputRules.IsAssignableToUsers(name).Should().BeFalse();
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~UserStatusesTests|FullyQualifiedName~RoleAssignabilityTests"`
Expected: erro de compilação — `UserStatuses` e `IsAssignableToUsers` não existem.

- [ ] **Step 3: Implementar**

`src/SecureGate/Secco.SecureGate.Application/Users/UserStatuses.cs`:

```csharp
namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Situação de uma conta como o admin a vê. Strings, e não enum, de propósito: o contrato OpenAPI
/// sai sem <c>type: string</c> para enum e o client NSwag quebra na desserialização (Fase 7.3).
/// </summary>
public static class UserStatuses
{
	/// <summary>Conta utilizável.</summary>
	public const string Active = "Active";

	/// <summary>Desativada pelo admin (lockout sem fim).</summary>
	public const string Deactivated = "Deactivated";

	/// <summary>Bloqueada temporariamente por tentativas de senha.</summary>
	public const string LockedOut = "LockedOut";

	/// <summary>Ano a partir do qual o lockout é tratado como desativação.</summary>
	private const int DeactivatedFromYear = 9999;

	/// <summary>Deriva a situação a partir do lockout do Identity.</summary>
	/// <param name="lockoutEnabled">Se o lockout está habilitado — desligado, o Identity ignora a data.</param>
	/// <param name="lockoutEnd">Fim do bloqueio.</param>
	/// <param name="now">Instante de referência.</param>
	public static string From(bool lockoutEnabled, DateTimeOffset? lockoutEnd, DateTimeOffset now)
	{
		if (!lockoutEnabled || lockoutEnd is not { } end || end <= now)
		{
			return Active;
		}

		// A desativação grava DateTimeOffset.MaxValue. Compara pelo ano, e não por igualdade: o
		// PostgreSQL guarda microssegundos e devolve o máximo truncado, que deixaria de ser igual.
		return end.Year >= DeactivatedFromYear ? Deactivated : LockedOut;
	}
}
```

Em `src/SecureGate/Secco.SecureGate.Application/Roles/RoleInputRules.cs`, logo depois de `IsReservedName`:

```csharp
	/// <summary>
	/// Indica se usuários podem ser membros do perfil. Dos reservados, só o de operador de instalação:
	/// os demais são identidades exclusivas de token (ADR-0031) — um usuário membro de
	/// <see cref="SecureGatePlatform.ElevatedLogReaderRole"/> receberia o read-set cross-tenant pelo nome.
	/// </summary>
	/// <param name="name">Nome já aparado.</param>
	public static bool IsAssignableToUsers(string name) =>
		!IsReservedName(name)
		|| string.Equals(name, SecureGatePlatform.OperatorRole, StringComparison.OrdinalIgnoreCase);
```

Em `src/SecureGate/Secco.SecureGate.Application/SecureGateErrors.cs`:

1. Na classe `Roles`, depois de `TooManyPermissions`:

```csharp

		/// <summary>Perfis reservados sustentam a estrutura da instalação e nunca são excluídos.</summary>
		public static readonly Error CannotDeleteReserved =
			Error.Conflict("SecureGate.Role.CannotDeleteReserved",
				"Perfis reservados da plataforma não podem ser excluídos.");

		/// <summary>
		/// Excluir perfil com membros retiraria o acesso de todos num clique irreversível; esvaziar
		/// primeiro é ato explícito.
		/// </summary>
		public static readonly Error HasMembers =
			Error.Conflict("SecureGate.Role.HasMembers",
				"O perfil tem membros. Remova-os antes de excluir.");
```

2. Na classe `Users`, o bloco hoje tem um `/// <summary>Algum role informado não existe no tenant.</summary>` órfão acima do resumo de `NotFound`. Substituir o trecho que vai de `/// <summary>Algum role informado não existe no tenant.</summary>` até a declaração de `RoleNotFound` inclusive por:

```csharp
		/// <summary>Usuário inexistente, ou pertencente a outro tenant — a resposta é a mesma de propósito.</summary>
		public static readonly Error NotFound =
			Error.NotFound("SecureGate.User.NotFound", "Usuário não encontrado.");

		/// <summary>
		/// Ninguém desativa a própria conta: trancaria o chamador para fora e, sendo ele o último
		/// operador, a instalação inteira.
		/// </summary>
		public static readonly Error CannotDeactivateSelf =
			Error.Conflict("SecureGate.User.CannotDeactivateSelf", "Não é possível desativar a própria conta.");

		/// <summary>Algum role informado não existe no tenant.</summary>
		public static readonly Error RoleNotFound =
			Error.Validation("SecureGate.User.RoleNotFound", "Um dos roles informados não existe neste tenant.");

		/// <summary>
		/// Perfil reservado que é identidade só de token (ADR-0031): nenhum usuário pode ser membro.
		/// </summary>
		public static readonly Error RoleNotAssignable =
			Error.Validation("SecureGate.User.RoleNotAssignable",
				"Este perfil é reservado à plataforma e não pode ser atribuído a usuários.");

		/// <summary>Remover a si mesmo do perfil de operador trancaria o chamador para fora.</summary>
		public static readonly Error CannotRemoveSelfFromOperator =
			Error.Conflict("SecureGate.User.CannotRemoveSelfFromOperator",
				"Não é possível remover a si mesmo do perfil de operador da instalação.");

		/// <summary>
		/// A operação deixaria a instalação sem operador ativo — reativar exige token de operador, então
		/// não haveria caminho de volta pela API.
		/// </summary>
		public static readonly Error LastActiveOperator =
			Error.Conflict("SecureGate.User.LastActiveOperator",
				"A operação deixaria a instalação sem nenhum operador ativo.");
```

- [ ] **Step 4: Rodar e ver passar**

Run: o mesmo comando do Step 2.
Expected: PASS, 13 testes.

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Application tests/SecureGate/Secco.SecureGate.Tests/Unit
git commit -m "feat(securegate): regras de status de conta e perfis atribuíveis

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: A renovação reaplica o filtro de operador (defeito)

Vem antes de qualquer endpoint de remoção, para nunca existir um commit em que retirar um operador do perfil seja possível e ele siga renovando `securegate:admin`.

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Api/Identity/InstallationOperatorPolicy.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Identity/OidcPrincipalBuilder.cs:45-46`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/InteractiveEndpoints.cs:77-98`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/TokenEndpoints.cs:123-125`
- Create: `tests/SecureGate/Secco.SecureGate.Tests/Integration/OidcLoginDriver.cs`
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Integration/SessionRevocationTests.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/OperatorDemotionTests.cs`

**Interfaces:**
- Produces: `InstallationOperatorPolicy.IsInstallationOperator(User user, IEnumerable<string> roles) : bool`; `InstallationOperatorPolicy.FilterScopes(User user, IEnumerable<string> roles, IEnumerable<string> scopes) : IReadOnlyList<string>`.
- Produces (testes): `OidcLoginDriver(SecureGateApiFactory factory, string clientId, string redirectUri, string password)` com `CreateBrowser()`, `BearerClient(string accessToken)`, `LoginAsync(string email, string scope) : Task<(string AccessToken, string RefreshToken)>`, `RefreshAsync(string refreshToken) : Task<HttpResponseMessage>`, `ObtainCodeAsync(HttpClient browser, string email, string scope) : Task<(string Verifier, string Code)>`, `SubmitLoginAsync(HttpClient browser, string email, string scope, string challenge) : Task<HttpResponseMessage>`, `ExchangeCodeAsync(HttpClient browser, string code, string verifier) : Task<HttpResponseMessage>`, `static CreatePkce() : (string Verifier, string Challenge)`, `static Scopes(string accessToken) : IReadOnlyList<string>`.

- [ ] **Step 1: Extrair o driver de login dos testes de revogação**

`tests/SecureGate/Secco.SecureGate.Tests/Integration/OidcLoginDriver.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Dirige o fluxo real do navegador contra o SecureGate — authorize → tela de login (antiforgery)
/// → code → troca com PKCE → refresh — para os testes obterem tokens EMITIDOS de verdade.
/// </summary>
internal sealed partial class OidcLoginDriver(
	SecureGateApiFactory factory,
	string clientId,
	string redirectUri,
	string password)
{
	[GeneratedRegex("__RequestVerificationToken.*?value=\"([^\"]+)\"", RegexOptions.Singleline)]
	private static partial Regex AntiforgeryField();

	public HttpClient CreateBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions
	{
		AllowAutoRedirect = false,
		HandleCookies = true,
	});

	public HttpClient BearerClient(string accessToken)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		return client;
	}

	public async Task<(string AccessToken, string RefreshToken)> LoginAsync(string email, string scope)
	{
		using var browser = CreateBrowser();
		var (verifier, code) = await ObtainCodeAsync(browser, email, scope);

		var response = await ExchangeCodeAsync(browser, code, verifier);
		var body = await response.Content.ReadAsStringAsync();
		response.StatusCode.Should().Be(HttpStatusCode.OK, body);

		using var json = JsonDocument.Parse(body);

		return (json.RootElement.GetProperty("access_token").GetString()!, json.RootElement.GetProperty("refresh_token").GetString()!);
	}

	public async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
	{
		using var client = factory.CreateClient();

		return await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "refresh_token",
			["refresh_token"] = refreshToken,
			["client_id"] = clientId,
		}));
	}

	public async Task<(string Verifier, string Code)> ObtainCodeAsync(HttpClient browser, string email, string scope)
	{
		var (verifier, challenge) = CreatePkce();

		var loginPost = await SubmitLoginAsync(browser, email, scope, challenge);
		loginPost.StatusCode.Should().Be(HttpStatusCode.Redirect, "credenciais válidas voltam ao authorize");

		var codeResponse = await browser.GetAsync(loginPost.Headers.Location!.ToString());
		codeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

		return (verifier, QueryHelpers.ParseQuery(codeResponse.Headers.Location!.Query)["code"].ToString());
	}

	public async Task<HttpResponseMessage> SubmitLoginAsync(HttpClient browser, string email, string scope, string challenge)
	{
		var authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
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

		var loginUrl = (await browser.GetAsync(authorizeUrl)).Headers.Location!.ToString();
		var loginPage = await browser.GetAsync(loginUrl);
		loginPage.EnsureSuccessStatusCode();
		var antiforgery = AntiforgeryField().Match(await loginPage.Content.ReadAsStringAsync()).Groups[1].Value;

		return await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["Input.Email"] = email,
			["Input.Password"] = password,
			["__RequestVerificationToken"] = antiforgery,
		}));
	}

	public Task<HttpResponseMessage> ExchangeCodeAsync(HttpClient browser, string code, string verifier) =>
		browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code",
			["code"] = code,
			["redirect_uri"] = redirectUri,
			["client_id"] = clientId,
			["code_verifier"] = verifier,
		}));

	public static (string Verifier, string Challenge) CreatePkce()
	{
		var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));

		return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
	}

	/// <summary>Scopes do access token — o OpenIddict os emite numa claim separada por espaços.</summary>
	public static IReadOnlyList<string> Scopes(string accessToken) =>
	[
		.. new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Claims
			.Where(claim => claim.Type == "scope")
			.SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
	];

	private static string Base64Url(byte[] bytes) =>
		Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

Em `SessionRevocationTests.cs`:
1. Trocar `public partial class SessionRevocationTests` por `public class SessionRevocationTests`.
2. Remover o `[GeneratedRegex]`/`AntiforgeryField()` e os métodos privados `BearerClient`, `CreateBrowser`, `RefreshAsync`, `LoginAsync`, `ObtainCodeAsync`, `SubmitLoginAsync`, `ExchangeCodeAsync`, `CreatePkce` e `Base64Url` (manter `NewUser`, `DeactivateUserUrl`, `AssertRefusedAsync` e `OperatorClientAsync`).
3. Depois dos campos `_otherTenantId`, adicionar:

```csharp
	private OidcLoginDriver Driver => new(secureGate, ClientId, RedirectUri, Password);
```

4. Substituir as chamadas: `LoginAsync(` → `Driver.LoginAsync(`, `RefreshAsync(` → `Driver.RefreshAsync(`, `CreateBrowser()` → `Driver.CreateBrowser()`, `BearerClient(` → `Driver.BearerClient(`, `ObtainCodeAsync(` → `Driver.ObtainCodeAsync(`, `ExchangeCodeAsync(` → `Driver.ExchangeCodeAsync(`, `SubmitLoginAsync(` → `Driver.SubmitLoginAsync(`, `CreatePkce()` → `OidcLoginDriver.CreatePkce()`.
5. Remover `using` que ficarem sem uso (`System.Security.Cryptography`, `System.Text`, `System.Text.RegularExpressions`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.AspNetCore.WebUtilities`).

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~SessionRevocationTests"`
Expected: PASS, 13 testes (refatoração sem mudança de comportamento).

- [ ] **Step 2: Escrever o teste do defeito**

`tests/SecureGate/Secco.SecureGate.Tests/Integration/OperatorDemotionTests.cs`:

```csharp
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
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~OperatorDemotionTests"`
Expected: `Refresh_OperadorNoPerfil_MantemScopeAdminSemTenant` PASS; os outros dois FAIL — o token renovado ainda contém `securegate:admin`.

- [ ] **Step 4: Implementar a política única**

`src/SecureGate/Secco.SecureGate.Api/Identity/InstallationOperatorPolicy.cs`:

```csharp
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Quem é operador de instalação e o que isso concede no token (ADR-0023/0024). Um lugar só, usado no
/// login, na renovação e na montagem do principal — era a ausência dele na renovação que deixava um
/// operador rebaixado renovando <c>securegate:admin</c>.
/// </summary>
internal static class InstallationOperatorPolicy
{
	/// <summary>
	/// Operador é quem tem o perfil de operador E está no tenant de plataforma. O nome do perfil é único
	/// só por tenant: exigir o tenant impede que um homônimo num tenant de cliente escale.
	/// </summary>
	/// <param name="user">Conta.</param>
	/// <param name="roles">Perfis atuais da conta.</param>
	public static bool IsInstallationOperator(User user, IEnumerable<string> roles)
	{
		ArgumentNullException.ThrowIfNull(user);

		return user.TenantId == SecureGatePlatform.TenantId
			&& roles.Contains(SecureGatePlatform.OperatorRole, StringComparer.Ordinal);
	}

	/// <summary>Remove <c>securegate:admin</c> dos scopes de quem não é operador.</summary>
	/// <param name="user">Conta.</param>
	/// <param name="roles">Perfis atuais da conta.</param>
	/// <param name="scopes">Scopes pedidos (login) ou herdados (renovação).</param>
	public static IReadOnlyList<string> FilterScopes(User user, IEnumerable<string> roles, IEnumerable<string> scopes)
	{
		var isOperator = IsInstallationOperator(user, roles);

		return [.. scopes.Where(scope => isOperator || scope != SecureGateScopes.Admin)];
	}
}
```

Em `OidcPrincipalBuilder.ForUser`, substituir:

```csharp
		var isInstallationOperator = user.TenantId == SecureGatePlatform.TenantId
			&& roleList.Contains(SecureGatePlatform.OperatorRole, StringComparer.Ordinal);
```

por:

```csharp
		var isInstallationOperator = InstallationOperatorPolicy.IsInstallationOperator(user, roleList);
```

Em `InteractiveEndpoints.cs`, substituir o trecho de `var scopes = request.GetScopes().AsEnumerable();` até `var granted = scopes.ToList();` (inclusive) por:

```csharp
		var granted = InstallationOperatorPolicy.FilterScopes(user, roles, request.GetScopes());
```

(o comentário `// ADR-0023: o scope admin só é emitido a operadores...` acima permanece.)

Em `TokenEndpoints.HandleUserGrantAsync`, substituir:

```csharp
		var scopes = stored!.GetScopes();
		var resources = await OidcPrincipalBuilder.ResolveResourcesAsync(scopeManager, scopes, context.RequestAborted);
		var principal = OidcPrincipalBuilder.ForUser(user, await userManager.GetRolesAsync(user), scopes, resources);
```

por:

```csharp
		// Os scopes também são re-derivados, e não copiados do token anterior: sem o filtro aqui, um
		// operador retirado do perfil seguiria renovando securegate:admin indefinidamente.
		var roles = await userManager.GetRolesAsync(user);
		var scopes = InstallationOperatorPolicy.FilterScopes(user, roles, stored!.GetScopes());
		var resources = await OidcPrincipalBuilder.ResolveResourcesAsync(scopeManager, scopes, context.RequestAborted);
		var principal = OidcPrincipalBuilder.ForUser(user, roles, scopes, resources);
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~OperatorDemotionTests|FullyQualifiedName~OperatorScopeFilterTests|FullyQualifiedName~AuthorizationCodeFlowTests|FullyQualifiedName~SessionRevocationTests|FullyQualifiedName~TokenExchangeElevationTests"`
Expected: PASS em todos.

- [ ] **Step 6: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Api tests/SecureGate/Secco.SecureGate.Tests/Integration
git commit -m "fix(securegate): renovação reaplica o filtro de operador aos scopes

A renovação re-derivava os papéis mas copiava os scopes do token anterior:
um operador retirado do perfil seguiria renovando securegate:admin para
sempre. InstallationOperatorPolicy passa a ser a regra única de login,
renovação e montagem do principal.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Ver perfil e listar membros

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Roles/RoleDetailDto.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Roles/IRoleRepository.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Roles/GetRoleHandler.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Roles/ListRoleMembersHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Roles/RoleRepository.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/RoleEndpoints.cs`
- Create: `tests/SecureGate/Secco.SecureGate.Tests/Integration/IdentitySeed.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/RoleProfileManagementTests.cs`

**Interfaces:**
- Consumes: `UserStatuses.From` (Task 1); `GetRolePermissionsHandler.HandleAsync(Guid tenantId, string? roleName, CancellationToken)` (existente).
- Produces: `RoleDetailDto(string Name, IReadOnlyList<string> Permissions, bool IsReserved, int MemberCount)`; `RoleMemberDto(Guid UserId, string Email, string Status)`; `RoleSummaryData(Guid Id, string Name, int MemberCount)`; `RoleMemberData(Guid UserId, string Email, bool LockoutEnabled, DateTimeOffset? LockoutEnd)`; `IRoleRepository.FindRoleAsync(Guid tenantId, string name, CancellationToken) : Task<RoleSummaryData?>`; `IRoleRepository.ListMembersAsync(Guid roleId, PageRequest page, CancellationToken) : Task<PagedResult<RoleMemberData>>`; rotas `GetRole` e `ListRoleMembers`.
- Produces (testes): `IdentitySeed.TenantAsync`, `RoleAsync`, `UserAsync`, `PlatformOperatorAsync`, `DeactivateAsync`, `LockOutAsync`, `AdminClient`.

- [ ] **Step 1: Seed de identidade para os testes**

`tests/SecureGate/Secco.SecureGate.Tests/Integration/IdentitySeed.cs`:

```csharp
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Monta tenants, perfis e usuários direto no banco — o que se testa é a API de gestão, não a
/// criação por ela.
/// </summary>
internal static class IdentitySeed
{
	public const string Password = "Perf1l@Secco!";

	/// <summary>Client com token HS256 de testes e <c>securegate:admin</c> — sub não é usuário (máquina).</summary>
	public static HttpClient AdminClient(SecureGateApiFactory factory)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.Admin));

		return client;
	}

	public static async Task<Guid> TenantAsync(SecureGateApiFactory factory)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		var tenant = new Tenant("Tenant de perfis", $"t-{Guid.NewGuid():N}");
		context.Tenants.Add(tenant);
		await context.SaveChangesAsync();

		return tenant.Id;
	}

	public static async Task RoleAsync(SecureGateApiFactory factory, Guid tenantId, string name, params string[] permissions)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		var role = new Role
		{
			Id = Guid.CreateVersion7(),
			TenantId = tenantId,
			Name = name,
			NormalizedName = name.ToUpperInvariant(),
			ConcurrencyStamp = Guid.NewGuid().ToString(),
		};
		context.Roles.Add(role);
		context.RoleClaims.AddRange(permissions.Select(permission =>
			new RoleClaim { RoleId = role.Id, ClaimType = "permission", ClaimValue = permission }));
		await context.SaveChangesAsync();
	}

	public static async Task<Guid> UserAsync(SecureGateApiFactory factory, Guid tenantId, string email, params string[] roles)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		var user = new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = tenantId,
			UserName = email,
			Email = email,
			EmailConfirmed = true,
		};
		(await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();

		foreach (var roleName in roles)
		{
			var normalized = roleName.ToUpperInvariant();
			var role = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized);
			context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
		}

		await context.SaveChangesAsync();

		return user.Id;
	}

	public static Task<Guid> PlatformOperatorAsync(SecureGateApiFactory factory, string email) =>
		UserAsync(factory, SecureGatePlatform.TenantId, email, SecureGatePlatform.OperatorRole);

	public static Task DeactivateAsync(SecureGateApiFactory factory, Guid userId) =>
		SetLockoutAsync(factory, userId, DateTimeOffset.MaxValue);

	public static Task LockOutAsync(SecureGateApiFactory factory, Guid userId, DateTimeOffset until) =>
		SetLockoutAsync(factory, userId, until);

	private static async Task SetLockoutAsync(SecureGateApiFactory factory, Guid userId, DateTimeOffset until)
	{
		using var scope = factory.Services.CreateScope();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		var user = await userManager.FindByIdAsync(userId.ToString());
		(await userManager.SetLockoutEnabledAsync(user!, true)).Succeeded.Should().BeTrue();
		(await userManager.SetLockoutEndDateAsync(user!, until)).Succeeded.Should().BeTrue();
	}
}
```

- [ ] **Step 2: Escrever os testes que falham**

`tests/SecureGate/Secco.SecureGate.Tests/Integration/RoleProfileManagementTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Leitura, exclusão e membros de perfil (issue #26).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class RoleProfileManagementTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"perfil-{Guid.NewGuid():N}@secco.test";

	[Fact]
	public async Task GetRole_Existente_DevolvePermissoesEContagemDeMembros()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "financeiro-user", "documentos:read", "boletos:read");
		await IdentitySeed.UserAsync(factory, _tenantId, Email(), "financeiro-user");
		await IdentitySeed.UserAsync(factory, _tenantId, Email(), "financeiro-user");

		var role = await IdentitySeed.AdminClient(factory)
			.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/roles/financeiro-user", Json);

		role.GetProperty("name").GetString().Should().Be("financeiro-user");
		role.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
			.Should().BeEquivalentTo("boletos:read", "documentos:read");
		role.GetProperty("isReserved").GetBoolean().Should().BeFalse();
		role.GetProperty("memberCount").GetInt32().Should().Be(2);
	}

	[Fact]
	public async Task GetRole_OperadorDaInstalacao_MarcaReservadoComReadSetDaPlataforma()
	{
		var role = await IdentitySeed.AdminClient(factory).GetFromJsonAsync<JsonElement>(
			$"/api/v1/tenants/{SecureGatePlatform.TenantId}/roles/{SecureGatePlatform.OperatorRole}", Json);

		role.GetProperty("isReserved").GetBoolean().Should().BeTrue();
		role.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
			.Should().BeEquivalentTo(SecureGatePlatform.OperatorReadPermissions);
	}

	[Fact]
	public async Task GetRole_DeOutroTenant_Retorna404()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, otherTenant, "so-no-outro");

		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/roles/so-no-outro");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task GetRole_NomeInvalido_Retorna400()
	{
		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/roles/nome%20com%20espaco");

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task ListRoleMembers_PaginaEMostraSituacao()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor");
		await IdentitySeed.UserAsync(factory, _tenantId, "a-" + Email(), "leitor");
		await IdentitySeed.UserAsync(factory, _tenantId, "b-" + Email(), "leitor");
		var deactivated = await IdentitySeed.UserAsync(factory, _tenantId, "c-" + Email(), "leitor");
		await IdentitySeed.DeactivateAsync(factory, deactivated);
		var admin = IdentitySeed.AdminClient(factory);

		var first = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/roles/leitor/members?page=1&size=2", Json);
		var second = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/roles/leitor/members?page=2&size=2", Json);

		first.GetProperty("totalCount").GetInt64().Should().Be(3);
		first.GetProperty("items").GetArrayLength().Should().Be(2);
		second.GetProperty("items").GetArrayLength().Should().Be(1);
		second.GetProperty("items")[0].GetProperty("userId").GetGuid().Should().Be(deactivated);
		second.GetProperty("items")[0].GetProperty("status").GetString().Should().Be("Deactivated");
	}

	[Fact]
	public async Task ListRoleMembers_PerfilDeOutroTenant_Retorna404()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, otherTenant, "alheio");

		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/roles/alheio/members");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~RoleProfileManagementTests"`
Expected: FAIL — as rotas não existem (405/404 onde se espera 200/400).

- [ ] **Step 4: Implementar**

`src/SecureGate/Secco.SecureGate.Application/Roles/RoleDetailDto.cs`:

```csharp
namespace Secco.SecureGate.Application.Roles;

/// <summary>Perfil detalhado para a gestão.</summary>
/// <param name="Name">Nome (a claim curta <c>role</c> dos tokens).</param>
/// <param name="Permissions">Permissões EFETIVAS — as mesmas que os produtos recebem na resolução.</param>
/// <param name="IsReserved">Perfil reservado da plataforma (não editável nem excluível).</param>
/// <param name="MemberCount">Quantidade de usuários membros.</param>
public sealed record RoleDetailDto(string Name, IReadOnlyList<string> Permissions, bool IsReserved, int MemberCount);

/// <summary>Membro de um perfil.</summary>
/// <param name="UserId">Usuário.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Status">Situação (<see cref="Users.UserStatuses"/>).</param>
public sealed record RoleMemberDto(Guid UserId, string Email, string Status);
```

Em `IRoleRepository.cs`, acrescentar `using Secco.SharedKernel.Pagination;` e, antes da interface:

```csharp
/// <summary>Perfil localizado no tenant.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nome como gravado.</param>
/// <param name="MemberCount">Quantidade de membros.</param>
public sealed record RoleSummaryData(Guid Id, string Name, int MemberCount);

/// <summary>Membro de perfil com o lockout cru — a Application deriva a situação.</summary>
/// <param name="UserId">Usuário.</param>
/// <param name="Email">E-mail.</param>
/// <param name="LockoutEnabled">Lockout habilitado.</param>
/// <param name="LockoutEnd">Fim do bloqueio.</param>
public sealed record RoleMemberData(Guid UserId, string Email, bool LockoutEnabled, DateTimeOffset? LockoutEnd);
```

e dentro da interface:

```csharp
	/// <summary>Localiza o perfil por (tenant, nome); <c>null</c> se não existe NESTE tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="name">Nome já validado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<RoleSummaryData?> FindRoleAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);

	/// <summary>Página de membros de um perfil já localizado, por e-mail.</summary>
	/// <param name="roleId">Perfil vindo de <see cref="FindRoleAsync"/> — nunca de input externo.</param>
	/// <param name="page">Página.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PagedResult<RoleMemberData>> ListMembersAsync(Guid roleId, PageRequest page, CancellationToken cancellationToken = default);
```

`src/SecureGate/Secco.SecureGate.Application/Roles/GetRoleHandler.cs`:

```csharp
using Secco.SecureGate.Application.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Roles;

/// <summary>
/// Detalha um perfil. As permissões vêm da MESMA resolução que os produtos consultam — inclusive os
/// casos especiais de perfil reservado —, para a tela nunca mostrar uma segunda interpretação.
/// </summary>
public sealed class GetRoleHandler(IRoleRepository repository, GetRolePermissionsHandler permissions)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="roleName">Nome do perfil (rota).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<RoleDetailDto>> HandleAsync(Guid tenantId, string? roleName, CancellationToken cancellationToken = default)
	{
		var name = roleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return SecureGateErrors.Roles.NameInvalid;
		}

		var role = await repository.FindRoleAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

		if (role is null)
		{
			return SecureGateErrors.Roles.NotFound;
		}

		var granted = await permissions.HandleAsync(tenantId, role.Name, cancellationToken).ConfigureAwait(false);

		return new RoleDetailDto(
			role.Name,
			granted.IsSuccess ? granted.Value : [],
			RoleInputRules.IsReservedName(role.Name),
			role.MemberCount);
	}
}
```

`src/SecureGate/Secco.SecureGate.Application/Roles/ListRoleMembersHandler.cs`:

```csharp
using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Roles;

/// <summary>Lista, paginados, os membros de um perfil do tenant.</summary>
public sealed class ListRoleMembersHandler(IRoleRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="roleName">Nome do perfil (rota).</param>
	/// <param name="page">Página pedida (já limitada pelo SharedKernel).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PagedResult<RoleMemberDto>>> HandleAsync(
		Guid tenantId,
		string? roleName,
		PageRequest page,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(page);

		var name = roleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return SecureGateErrors.Roles.NameInvalid;
		}

		// O perfil é localizado no tenant ANTES: a consulta de membros só recebe um id que já passou
		// pelo isolamento, nunca o nome cru
		var role = await repository.FindRoleAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

		if (role is null)
		{
			return SecureGateErrors.Roles.NotFound;
		}

		var members = await repository.ListMembersAsync(role.Id, page, cancellationToken).ConfigureAwait(false);
		var now = DateTimeOffset.UtcNow;

		return PagedResult.Create<RoleMemberDto>(
			[.. members.Items.Select(member => new RoleMemberDto(
				member.UserId,
				member.Email,
				UserStatuses.From(member.LockoutEnabled, member.LockoutEnd, now)))],
			page,
			members.TotalCount);
	}
}
```

Em `SecureGateApplicationExtensions.cs`, depois de `services.AddScoped<GetRolePermissionsHandler>();`:

```csharp
		services.AddScoped<GetRoleHandler>();
		services.AddScoped<ListRoleMembersHandler>();
```

Em `RoleRepository.cs`, acrescentar `using Secco.SharedKernel.Pagination;` e os métodos:

```csharp
	public async Task<RoleSummaryData?> FindRoleAsync(Guid tenantId, string name, CancellationToken cancellationToken = default)
	{
		var normalized = Normalize(name);

		return await context.Roles
			.AsNoTracking()
			.Where(r => r.TenantId == tenantId && r.NormalizedName == normalized)
			.Select(r => new RoleSummaryData(r.Id, r.Name!, context.UserRoles.Count(ur => ur.RoleId == r.Id)))
			.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<PagedResult<RoleMemberData>> ListMembersAsync(
		Guid roleId, PageRequest page, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(page);

		var members =
			from userRole in context.UserRoles.AsNoTracking()
			join user in context.Users.AsNoTracking() on userRole.UserId equals user.Id
			where userRole.RoleId == roleId
			select user;

		var total = await members.LongCountAsync(cancellationToken).ConfigureAwait(false);

		var items = await members
			.OrderBy(user => user.Email)
			.ThenBy(user => user.Id)
			.Skip(page.Skip)
			.Take(page.Size)
			.Select(user => new RoleMemberData(user.Id, user.Email!, user.LockoutEnabled, user.LockoutEnd))
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		return PagedResult.Create<RoleMemberData>(items, page, total);
	}
```

Em `RoleEndpoints.cs`, acrescentar `using Secco.SharedKernel.Pagination;` e, antes de `return endpoints;`:

```csharp
		group.MapGet("/{role}", async (
				Guid tenantId,
				string role,
				GetRoleHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, role, cancellationToken))
				.ToHttpResult(dto => Results.Ok(dto)))
			.WithName("GetRole")
			.WithSummary("Detalha um perfil: permissões efetivas, se é reservado e quantos membros tem.")
			.Produces<RoleDetailDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/{role}/members", async (
				Guid tenantId,
				string role,
				int? page,
				int? size,
				ListRoleMembersHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				tenantId,
				role,
				new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize),
				cancellationToken))
				.ToHttpResult(result => Results.Ok(result)))
			.WithName("ListRoleMembers")
			.WithSummary("Lista, paginados, os membros do perfil com a situação de cada conta.")
			.Produces<PagedResult<RoleMemberDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);
```

- [ ] **Step 5: Rodar e ver passar**

Run: o mesmo comando do Step 3.
Expected: PASS, 6 testes.

- [ ] **Step 6: Commit**

```bash
git add src/SecureGate tests/SecureGate
git commit -m "feat(securegate): detalhe de perfil e membros paginados (#26)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Excluir perfil

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/Roles/IRoleRepository.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Roles/DeleteRoleHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Roles/RoleRepository.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/RoleEndpoints.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/RoleProfileManagementTests.cs`

**Interfaces:**
- Consumes: `SecureGateErrors.Roles.CannotDeleteReserved`, `Roles.HasMembers` (Task 1); `IdentitySeed` (Task 3).
- Produces: `enum DeleteRoleOutcome { Deleted, NotFound, HasMembers }`; `IRoleRepository.DeleteRoleAsync(Guid tenantId, string name, CancellationToken) : Task<DeleteRoleOutcome>`; rota `DeleteRole`.

- [ ] **Step 1: Escrever os testes que falham**

Acrescentar a `RoleProfileManagementTests` (usings: `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.DependencyInjection`, `Secco.SecureGate.Infrastructure.Contexts`):

```csharp
	[Fact]
	public async Task DeleteRole_SemMembros_ExcluiPerfilEPermissoes()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "temporario", "relatorios:read");
		var admin = IdentitySeed.AdminClient(factory);

		(await admin.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/temporario")).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await admin.GetAsync($"/api/v1/tenants/{_tenantId}/roles/temporario")).StatusCode.Should().Be(HttpStatusCode.NotFound);

		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.RoleClaims.CountAsync(c => c.ClaimValue == "relatorios:read"
			&& !context.Roles.Any(r => r.Id == c.RoleId))).Should().Be(0, "permissões órfãs não podem sobrar");
	}

	[Fact]
	public async Task DeleteRole_ComMembros_Retorna409EMantem()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "ocupado");
		await IdentitySeed.UserAsync(factory, _tenantId, Email(), "ocupado");
		var admin = IdentitySeed.AdminClient(factory);

		(await admin.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/ocupado")).StatusCode.Should().Be(HttpStatusCode.Conflict);

		(await admin.GetAsync($"/api/v1/tenants/{_tenantId}/roles/ocupado")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task DeleteRole_OperadorDaInstalacao_Retorna409()
	{
		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/roles/{SecureGatePlatform.OperatorRole}");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task DeleteRole_Inexistente_Retorna404()
	{
		var response = await IdentitySeed.AdminClient(factory).DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/nao-existe");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task DeleteRole_DeOutroTenant_Retorna404ENaoExclui()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, otherTenant, "do-vizinho");
		var admin = IdentitySeed.AdminClient(factory);

		(await admin.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/do-vizinho")).StatusCode.Should().Be(HttpStatusCode.NotFound);

		(await admin.GetAsync($"/api/v1/tenants/{otherTenant}/roles/do-vizinho")).StatusCode.Should().Be(HttpStatusCode.OK);
	}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~RoleProfileManagementTests.DeleteRole"`
Expected: FAIL — `DELETE` responde 405.

- [ ] **Step 3: Implementar**

Em `IRoleRepository.cs`, antes da interface:

```csharp
/// <summary>Resultado da exclusão de perfil.</summary>
public enum DeleteRoleOutcome
{
	/// <summary>Perfil e permissões excluídos.</summary>
	Deleted,

	/// <summary>Perfil não existe neste tenant.</summary>
	NotFound,

	/// <summary>Perfil tem membros; nada foi alterado.</summary>
	HasMembers,
}
```

e na interface:

```csharp
	/// <summary>
	/// Exclui o perfil e suas permissões se não tiver membros. A checagem de membros é refeita aqui, junto
	/// da exclusão, para encurtar a janela entre verificar e excluir.
	/// </summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="name">Nome já validado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<DeleteRoleOutcome> DeleteRoleAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);
```

`src/SecureGate/Secco.SecureGate.Application/Roles/DeleteRoleHandler.cs`:

```csharp
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Roles;

/// <summary>Exclui um perfil vazio do tenant. Perfis reservados nunca são excluídos.</summary>
public sealed class DeleteRoleHandler(IRoleRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="roleName">Nome do perfil (rota).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, string? roleName, CancellationToken cancellationToken = default)
	{
		var name = roleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return Result.Failure(SecureGateErrors.Roles.NameInvalid);
		}

		// Antes do banco: a lista de reservados é pública, e o de operador sustenta a instalação
		if (RoleInputRules.IsReservedName(name))
		{
			return Result.Failure(SecureGateErrors.Roles.CannotDeleteReserved);
		}

		return await repository.DeleteRoleAsync(tenantId, name, cancellationToken).ConfigureAwait(false) switch
		{
			DeleteRoleOutcome.Deleted => Result.Success(),
			DeleteRoleOutcome.HasMembers => Result.Failure(SecureGateErrors.Roles.HasMembers),
			_ => Result.Failure(SecureGateErrors.Roles.NotFound),
		};
	}
}
```

Em `SecureGateApplicationExtensions.cs`: `services.AddScoped<DeleteRoleHandler>();` junto dos handlers de roles.

Em `RoleRepository.cs`:

```csharp
	public async Task<DeleteRoleOutcome> DeleteRoleAsync(Guid tenantId, string name, CancellationToken cancellationToken = default)
	{
		var normalized = Normalize(name);

		var role = await context.Roles
			.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized, cancellationToken)
			.ConfigureAwait(false);

		if (role is null)
		{
			return DeleteRoleOutcome.NotFound;
		}

		if (await context.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, cancellationToken).ConfigureAwait(false))
		{
			return DeleteRoleOutcome.HasMembers;
		}

		// Permissões removidas explicitamente — não depende do comportamento de cascata da FK
		context.RoleClaims.RemoveRange(await context.RoleClaims
			.Where(c => c.RoleId == role.Id)
			.ToListAsync(cancellationToken).ConfigureAwait(false));
		context.Roles.Remove(role);

		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return DeleteRoleOutcome.Deleted;
	}
```

Em `RoleEndpoints.cs`, antes de `return endpoints;`:

```csharp
		group.MapDelete("/{role}", async (
				Guid tenantId,
				string role,
				DeleteRoleHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, role, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("DeleteRole")
			.WithSummary("Exclui um perfil sem membros. Perfis reservados da plataforma não são excluíveis.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~RoleProfileManagementTests"`
Expected: PASS, 11 testes.

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate tests/SecureGate
git commit -m "feat(securegate): excluir perfil vazio, nunca reservado (#26)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Detalhe do usuário e situação na listagem

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/UserDto.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Users/UserDetailDto.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/IUserDirectory.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Users/GetUserHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Users/UserAccountService.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/UserEndpoints.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/UserProfileManagementTests.cs`

**Interfaces:**
- Consumes: `UserStatuses` (Task 1); `GetRolePermissionsHandler` (existente); `IdentitySeed` (Task 3).
- Produces: `UserDto(Guid Id, string Email, Guid TenantId, IReadOnlyList<string> Roles, string Status)`; `UserDetailDto(Guid Id, string Email, Guid TenantId, string Status, DateTimeOffset? LockoutEnd, IReadOnlyList<string> Roles, IReadOnlyList<string> EffectivePermissions, IReadOnlyList<string> ExternalLogins)`; `UserAccountData(Guid Id, string Email, Guid TenantId, bool LockoutEnabled, DateTimeOffset? LockoutEnd, IReadOnlyList<string> Roles, IReadOnlyList<string> ExternalLogins)`; `IUserDirectory.GetAsync(Guid tenantId, Guid userId, CancellationToken) : Task<UserAccountData?>`; rota `GetUser`.

- [ ] **Step 1: Escrever os testes que falham**

`tests/SecureGate/Secco.SecureGate.Tests/Integration/UserProfileManagementTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Detalhe do usuário, atribuição e remoção de perfis (issue #26).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class UserProfileManagementTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"usuario-{Guid.NewGuid():N}@secco.test";

	private Task<JsonElement> GetUserAsync(Guid userId) =>
		IdentitySeed.AdminClient(factory).GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/users/{userId}", Json);

	private static IReadOnlyList<string?> Strings(JsonElement element, string property) =>
		[.. element.GetProperty(property).EnumerateArray().Select(item => item.GetString())];

	[Fact]
	public async Task GetUser_Ativo_DevolvePerfisEPermissoesEfetivasSemRepeticao()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor", "documentos:read");
		await IdentitySeed.RoleAsync(factory, _tenantId, "editor", "documentos:read", "documentos:write");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email(), "leitor", "editor");

		var user = await GetUserAsync(userId);

		user.GetProperty("status").GetString().Should().Be("Active");
		user.GetProperty("lockoutEnd").ValueKind.Should().Be(JsonValueKind.Null);
		Strings(user, "roles").Should().BeEquivalentTo("editor", "leitor");
		Strings(user, "effectivePermissions").Should().Equal("documentos:read", "documentos:write");
	}

	[Fact]
	public async Task GetUser_PermissoesEfetivas_IguaisAResolucaoDosProdutos()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "auditor-interno", "relatorios:read", "trilha:read");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email(), "auditor-interno");

		using var resolver = factory.CreateClient();
		resolver.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.AuthorizationRead));
		var resolved = await resolver.GetFromJsonAsync<List<string>>(
			$"/api/v1/authorization/tenants/{_tenantId}/roles/auditor-interno/permissions", Json);

		Strings(await GetUserAsync(userId), "effectivePermissions").Should().BeEquivalentTo(resolved);
	}

	[Fact]
	public async Task GetUser_Desativado_Deactivated()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await IdentitySeed.DeactivateAsync(factory, userId);

		(await GetUserAsync(userId)).GetProperty("status").GetString().Should().Be("Deactivated");
	}

	[Fact]
	public async Task GetUser_BloqueadoPorTentativas_LockedOutComFim()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await IdentitySeed.LockOutAsync(factory, userId, DateTimeOffset.UtcNow.AddMinutes(5));

		var user = await GetUserAsync(userId);

		user.GetProperty("status").GetString().Should().Be("LockedOut");
		user.GetProperty("lockoutEnd").ValueKind.Should().Be(JsonValueKind.String);
	}

	[Fact]
	public async Task GetUser_ComLoginEntra_ListaSoOProvedor()
	{
		const string providerKey = "11111111-2222-3333-4444-555555555555:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		using (var scope = factory.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			context.UserLogins.Add(new UserLogin { UserId = userId, LoginProvider = "EntraId", ProviderKey = providerKey, ProviderDisplayName = "Microsoft" });
			await context.SaveChangesAsync();
		}

		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/users/{userId}");
		var body = await response.Content.ReadAsStringAsync();

		body.Should().NotContain("aaaaaaaa-bbbb", "o identificador do diretório nunca sai da API");
		Strings(JsonDocument.Parse(body).RootElement, "externalLogins").Should().Equal("EntraId");
	}

	[Fact]
	public async Task GetUser_DeOutroTenant_Retorna404()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		var stranger = await IdentitySeed.UserAsync(factory, otherTenant, Email());

		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/users/{stranger}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task GetUser_Inexistente_Retorna404()
	{
		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/users/{Guid.CreateVersion7()}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task ListUsers_MostraSituacao()
	{
		var deactivated = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await IdentitySeed.DeactivateAsync(factory, deactivated);

		var users = await IdentitySeed.AdminClient(factory)
			.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/users", Json);

		users.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == deactivated)
			.GetProperty("status").GetString().Should().Be("Deactivated");
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~UserProfileManagementTests"`
Expected: FAIL — rota `GET /users/{userId}` não existe; `status` ausente na listagem.

- [ ] **Step 3: Implementar**

`src/SecureGate/Secco.SecureGate.Application/Users/UserDto.cs` — substituir o record por:

```csharp
/// <summary>Usuário provisionado (sem segredos).</summary>
/// <param name="Id">Identificador (o <c>sub</c> dos tokens).</param>
/// <param name="Email">E-mail (também o username).</param>
/// <param name="TenantId">Tenant do usuário.</param>
/// <param name="Roles">Perfis do usuário no tenant.</param>
/// <param name="Status">Situação da conta (<see cref="UserStatuses"/>).</param>
public sealed record UserDto(Guid Id, string Email, Guid TenantId, IReadOnlyList<string> Roles, string Status);
```

(manter o `namespace` do arquivo.)

`src/SecureGate/Secco.SecureGate.Application/Users/UserDetailDto.cs`:

```csharp
namespace Secco.SecureGate.Application.Users;

/// <summary>Usuário detalhado para a gestão — responde "por que fulano tem acesso".</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="TenantId">Tenant.</param>
/// <param name="Status">Situação (<see cref="UserStatuses"/>).</param>
/// <param name="LockoutEnd">Fim do bloqueio, só quando <see cref="UserStatuses.LockedOut"/>.</param>
/// <param name="Roles">Perfis.</param>
/// <param name="EffectivePermissions">União das permissões dos perfis, pela resolução dos produtos.</param>
/// <param name="ExternalLogins">Provedores externos vinculados — só o nome, nunca o identificador.</param>
public sealed record UserDetailDto(
	Guid Id,
	string Email,
	Guid TenantId,
	string Status,
	DateTimeOffset? LockoutEnd,
	IReadOnlyList<string> Roles,
	IReadOnlyList<string> EffectivePermissions,
	IReadOnlyList<string> ExternalLogins);
```

Em `IUserDirectory.cs`, depois de `CreateUserData`:

```csharp
/// <summary>Conta com lockout cru e vínculos — a Application deriva situação e permissões.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="TenantId">Tenant.</param>
/// <param name="LockoutEnabled">Lockout habilitado.</param>
/// <param name="LockoutEnd">Fim do bloqueio.</param>
/// <param name="Roles">Perfis, por nome.</param>
/// <param name="ExternalLogins">Nomes dos provedores externos vinculados.</param>
public sealed record UserAccountData(
	Guid Id,
	string Email,
	Guid TenantId,
	bool LockoutEnabled,
	DateTimeOffset? LockoutEnd,
	IReadOnlyList<string> Roles,
	IReadOnlyList<string> ExternalLogins);
```

e na interface:

```csharp
	/// <summary>Conta do tenant; <c>null</c> se não existe ou é de outro tenant.</summary>
	/// <param name="tenantId">Tenant esperado.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<UserAccountData?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
```

`src/SecureGate/Secco.SecureGate.Application/Users/GetUserHandler.cs`:

```csharp
using Secco.SecureGate.Application.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Detalha um usuário com o acesso efetivo. As permissões saem da mesma resolução que os produtos
/// consultam, perfil a perfil — a tela mostra o que vale, não uma segunda interpretação.
/// </summary>
public sealed class GetUserHandler(IUserDirectory userDirectory, GetRolePermissionsHandler permissions)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<UserDetailDto>> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var account = await userDirectory.GetAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);

		if (account is null)
		{
			return SecureGateErrors.Users.NotFound;
		}

		var effective = new SortedSet<string>(StringComparer.Ordinal);

		foreach (var role in account.Roles)
		{
			var granted = await permissions.HandleAsync(tenantId, role, cancellationToken).ConfigureAwait(false);

			if (granted.IsSuccess)
			{
				effective.UnionWith(granted.Value);
			}
		}

		var status = UserStatuses.From(account.LockoutEnabled, account.LockoutEnd, DateTimeOffset.UtcNow);

		return new UserDetailDto(
			account.Id,
			account.Email,
			account.TenantId,
			status,
			status == UserStatuses.LockedOut ? account.LockoutEnd : null,
			account.Roles,
			[.. effective],
			account.ExternalLogins);
	}
}
```

Em `SecureGateApplicationExtensions.cs`: `services.AddScoped<GetUserHandler>();` junto dos handlers de usuário.

Em `UserAccountService.cs`:

1. No retorno de `CreateAsync`, substituir `new UserDto(user.Id, user.Email!, user.TenantId, [.. tenantRoles.Select(role => role.Name!)])` por `new UserDto(user.Id, user.Email!, user.TenantId, [.. tenantRoles.Select(role => role.Name!)], UserStatuses.Active)`.
2. Em `ListByTenantAsync`, antes do `return`, declarar `var now = DateTimeOffset.UtcNow;` e trocar a construção por:

```csharp
			.. users.Select(user => new UserDto(
				user.Id,
				user.Email!,
				user.TenantId,
				[.. roleAssignments.Where(a => a.UserId == user.Id).Select(a => a.Name!)],
				UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, now)))
```

3. Novo método:

```csharp
	public async Task<UserAccountData?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var user = await context.Users
			.AsNoTracking()
			.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, cancellationToken)
			.ConfigureAwait(false);

		if (user is null)
		{
			return null;
		}

		var roles = await (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			where userRole.UserId == userId && role.TenantId == tenantId
			orderby role.Name
			select role.Name!)
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		// Só o provedor: a chave (tid:oid do Entra) identifica a pessoa no diretório do cliente
		var logins = await context.UserLogins
			.AsNoTracking()
			.Where(login => login.UserId == userId)
			.Select(login => login.LoginProvider)
			.Distinct()
			.OrderBy(provider => provider)
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		return new UserAccountData(user.Id, user.Email!, user.TenantId, user.LockoutEnabled, user.LockoutEnd, roles, logins);
	}
```

Em `UserEndpoints.cs`, antes de `return endpoints;`:

```csharp
		group.MapGet("/{userId:guid}", async (
				Guid tenantId,
				Guid userId,
				GetUserHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(dto => Results.Ok(dto)))
			.WithName("GetUser")
			.WithSummary("Detalha o usuário: situação, perfis, permissões efetivas e logins externos (sem identificadores).")
			.Produces<UserDetailDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~UserProfileManagementTests|FullyQualifiedName~UserManagementTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate tests/SecureGate
git commit -m "feat(securegate): detalhe do usuário com acesso efetivo e situação (#26)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Atribuir perfil a usuário existente

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/IUserDirectory.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Users/AddUserRoleHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/CreateUserHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Users/UserAccountService.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/UserEndpoints.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/UserProfileManagementTests.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/ProfileAssignmentTokenTests.cs`

**Interfaces:**
- Consumes: `RoleInputRules.IsAssignableToUsers`, `SecureGateErrors.Users.RoleNotAssignable` (Task 1); `OidcLoginDriver` (Task 2); `IdentitySeed` (Task 3).
- Produces: `enum RoleAssignmentOutcome { Done, UserNotFound, RoleNotFound }`; `IUserDirectory.AddRoleAsync(Guid tenantId, Guid userId, string roleName, CancellationToken) : Task<RoleAssignmentOutcome>`; `AddUserRoleHandler.ToResult(RoleAssignmentOutcome) : Result` (internal static); rota `AddUserRole`.

- [ ] **Step 1: Escrever os testes que falham**

Acrescentar a `UserProfileManagementTests`:

```csharp
	[Fact]
	public async Task AddUserRole_AtribuiEApareceNoDetalhe()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "inventario-admin", "inventario:write");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		var response = await IdentitySeed.AdminClient(factory)
			.PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/roles/inventario-admin", null);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		Strings(await GetUserAsync(userId), "roles").Should().Equal("inventario-admin");
	}

	[Fact]
	public async Task AddUserRole_Repetido_Idempotente()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var admin = IdentitySeed.AdminClient(factory);
		var url = $"/api/v1/tenants/{_tenantId}/users/{userId}/roles/leitor";

		(await admin.PostAsync(url, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await admin.PostAsync(url, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		Strings(await GetUserAsync(userId), "roles").Should().Equal("leitor");
	}

	[Fact]
	public async Task AddUserRole_PerfilSoDeOutroTenant_Retorna404()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, otherTenant, "admin", "tudo:write");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		var response = await IdentitySeed.AdminClient(factory)
			.PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/roles/admin", null);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
		Strings(await GetUserAsync(userId), "roles").Should().BeEmpty("o perfil homônimo do vizinho não pode ser atribuído");
	}

	[Fact]
	public async Task AddUserRole_UsuarioDeOutroTenant_Retorna404()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor");
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		var stranger = await IdentitySeed.UserAsync(factory, otherTenant, Email());

		var response = await IdentitySeed.AdminClient(factory)
			.PostAsync($"/api/v1/tenants/{_tenantId}/users/{stranger}/roles/leitor", null);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task AddUserRole_PerfilInexistente_Retorna404()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		var response = await IdentitySeed.AdminClient(factory)
			.PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/roles/nao-existe", null);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task AddUserRole_NomeInvalido_Retorna400()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		var response = await IdentitySeed.AdminClient(factory)
			.PostAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/roles/nome%20invalido", null);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Theory]
	[InlineData(SecureGatePlatform.ElevatedLogReaderRole)]
	[InlineData(SecureGatePlatform.AuditorRole)]
	[InlineData(SecureGatePlatform.LegacyOperatorRole)]
	public async Task AddUserRole_PerfilSoDeToken_Retorna400(string reserved)
	{
		var userId = await IdentitySeed.UserAsync(factory, SecureGatePlatform.TenantId, Email());

		var response = await IdentitySeed.AdminClient(factory)
			.PostAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/users/{userId}/roles/{reserved}", null);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await response.Content.ReadAsStringAsync()).Should().Contain("SecureGate.User.RoleNotAssignable");
	}

	[Theory]
	[InlineData(SecureGatePlatform.ElevatedLogReaderRole)]
	[InlineData(SecureGatePlatform.AuditorRole)]
	[InlineData(SecureGatePlatform.LegacyOperatorRole)]
	public async Task CreateUser_ComPerfilSoDeToken_Retorna400(string reserved)
	{
		var response = await IdentitySeed.AdminClient(factory).PostAsJsonAsync(
			$"/api/v1/tenants/{SecureGatePlatform.TenantId}/users",
			new { email = Email(), password = IdentitySeed.Password, roles = new[] { reserved } });

		// O código importa: esses perfis não existem como linha, então sem a regra a resposta JÁ seria
		// 400 — mas por RoleNotFound, que deixaria de proteger no dia em que alguém os semeasse
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await response.Content.ReadAsStringAsync()).Should().Contain("SecureGate.User.RoleNotAssignable");
	}
```

`tests/SecureGate/Secco.SecureGate.Tests/Integration/ProfileAssignmentTokenTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~UserProfileManagementTests|FullyQualifiedName~ProfileAssignmentTokenTests"`
Expected: FAIL — a rota de atribuição não existe (405); `CreateUser_ComPerfilSoDeToken_Retorna400` responde 400, mas com `SecureGate.User.RoleNotFound`, e falha na asserção do código de erro.

- [ ] **Step 3: Implementar**

Em `IUserDirectory.cs`, antes da interface:

```csharp
/// <summary>Resultado de atribuir ou remover perfil.</summary>
public enum RoleAssignmentOutcome
{
	/// <summary>Estado final atingido (inclusive se já estava assim).</summary>
	Done,

	/// <summary>Usuário não existe neste tenant.</summary>
	UserNotFound,

	/// <summary>Perfil não existe neste tenant.</summary>
	RoleNotFound,
}
```

e na interface:

```csharp
	/// <summary>Torna o usuário membro do perfil, ambos no tenant. Idempotente.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="roleName">Nome do perfil já validado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<RoleAssignmentOutcome> AddRoleAsync(Guid tenantId, Guid userId, string roleName, CancellationToken cancellationToken = default);
```

`src/SecureGate/Secco.SecureGate.Application/Users/AddUserRoleHandler.cs`:

```csharp
using Secco.SecureGate.Application.Roles;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Atribui um perfil a um usuário que já existe (issue #26). Idempotente: repetir devolve sucesso, e
/// 404 significa sempre "perfil ou usuário não existe neste tenant", nunca "já estava assim".
/// </summary>
public sealed class AddUserRoleHandler(IUserDirectory userDirectory)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="roleName">Nome do perfil (rota).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid userId, string? roleName, CancellationToken cancellationToken = default)
	{
		var name = roleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return Result.Failure(SecureGateErrors.Roles.NameInvalid);
		}

		if (!RoleInputRules.IsAssignableToUsers(name))
		{
			return Result.Failure(SecureGateErrors.Users.RoleNotAssignable);
		}

		return ToResult(await userDirectory.AddRoleAsync(tenantId, userId, name, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>Traduz o resultado da persistência — compartilhado com a remoção.</summary>
	/// <param name="outcome">Resultado.</param>
	internal static Result ToResult(RoleAssignmentOutcome outcome) => outcome switch
	{
		RoleAssignmentOutcome.Done => Result.Success(),
		RoleAssignmentOutcome.UserNotFound => Result.Failure(SecureGateErrors.Users.NotFound),
		_ => Result.Failure(SecureGateErrors.Roles.NotFound),
	};
}
```

Em `CreateUserHandler.cs`, dentro do `foreach (var role in roles)`, **antes** da checagem de existência:

```csharp
			if (!RoleInputRules.IsAssignableToUsers(role))
			{
				return Result.Failure<UserDto>(SecureGateErrors.Users.RoleNotAssignable);
			}
```

Em `SecureGateApplicationExtensions.cs`: `services.AddScoped<AddUserRoleHandler>();`.

Em `UserAccountService.cs`:

```csharp
	public async Task<RoleAssignmentOutcome> AddRoleAsync(
		Guid tenantId, Guid userId, string roleName, CancellationToken cancellationToken = default)
	{
		if (!await BelongsToTenantAsync(tenantId, userId, cancellationToken).ConfigureAwait(false))
		{
			return RoleAssignmentOutcome.UserNotFound;
		}

		var roleId = await FindRoleIdAsync(tenantId, roleName, cancellationToken).ConfigureAwait(false);

		if (roleId is null)
		{
			return RoleAssignmentOutcome.RoleNotFound;
		}

		if (await IsAssignedAsync(userId, roleId.Value, cancellationToken).ConfigureAwait(false))
		{
			return RoleAssignmentOutcome.Done;
		}

		context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId.Value });

		try
		{
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (DbUpdateException)
		{
			// Duas atribuições simultâneas (clique duplo, retry): a segunda bate na PK. Se a linha existe,
			// o estado pedido foi atingido — idempotente; qualquer outra falha sobe.
			context.ChangeTracker.Clear();

			if (!await IsAssignedAsync(userId, roleId.Value, cancellationToken).ConfigureAwait(false))
			{
				throw;
			}
		}

		return RoleAssignmentOutcome.Done;
	}

	/// <summary>
	/// Perfil por (tenant, nome normalizado) — NUNCA por nome global: o nome só é único por tenant, e a
	/// busca global acharia o perfil homônimo de outro tenant.
	/// </summary>
	private Task<Guid?> FindRoleIdAsync(Guid tenantId, string roleName, CancellationToken cancellationToken)
	{
		var normalized = roleName.ToUpperInvariant();

		return context.Roles
			.AsNoTracking()
			.Where(role => role.TenantId == tenantId && role.NormalizedName == normalized)
			.Select(role => (Guid?)role.Id)
			.FirstOrDefaultAsync(cancellationToken);
	}

	private Task<bool> IsAssignedAsync(Guid userId, Guid roleId, CancellationToken cancellationToken) =>
		context.UserRoles
			.AsNoTracking()
			.AnyAsync(userRole => userRole.UserId == userId && userRole.RoleId == roleId, cancellationToken);
```

Em `UserEndpoints.cs`, antes de `return endpoints;`:

```csharp
		group.MapPost("/{userId:guid}/roles/{role}", async (
				Guid tenantId,
				Guid userId,
				string role,
				AddUserRoleHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, role, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("AddUserRole")
			.WithSummary("Torna o usuário membro do perfil (idempotente). Vale no token na próxima renovação.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~UserProfileManagementTests|FullyQualifiedName~ProfileAssignmentTokenTests|FullyQualifiedName~UserManagementTests|FullyQualifiedName~CreateUserPasswordLengthTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate tests/SecureGate
git commit -m "feat(securegate): atribuir perfil a usuário existente (#26)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Remover perfil e guardas do operador

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/IUserDirectory.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Users/OperatorGuard.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Users/RemoveUserRoleHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Users/SetUserActivationHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Users/UserAccountService.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/UserEndpoints.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/UserProfileManagementTests.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/ProfileAssignmentTokenTests.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/LastOperatorGuardTests.cs`

**Interfaces:**
- Consumes: `AddUserRoleHandler.ToResult`, `RoleAssignmentOutcome`, `FindRoleIdAsync` (Task 6); erros da Task 1; `IdentitySeed` (Task 3); `OidcLoginDriver` (Task 2).
- Produces: `IUserDirectory.RemoveRoleAsync(Guid tenantId, Guid userId, string roleName, CancellationToken) : Task<RoleAssignmentOutcome>`; `IUserDirectory.HasRoleAsync(Guid tenantId, Guid userId, string roleName, CancellationToken) : Task<bool>`; `IUserDirectory.CountActiveOperatorsAsync(Guid excludingUserId, CancellationToken) : Task<int>`; `RemoveUserRoleCommand(Guid TenantId, Guid UserId, string? RoleName, string? CallerSubject)`; rota `RemoveUserRole`.

- [ ] **Step 1: Escrever os testes que falham**

Acrescentar a `UserProfileManagementTests`:

```csharp
	[Fact]
	public async Task RemoveUserRole_RemoveESomeDoDetalhe()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email(), "leitor");

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/roles/leitor");

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		Strings(await GetUserAsync(userId), "roles").Should().BeEmpty();
	}

	[Fact]
	public async Task RemoveUserRole_NaoMembro_Idempotente()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/roles/leitor");

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task RemoveUserRole_UsuarioDeOutroTenant_Retorna404ENaoAltera()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, otherTenant, "leitor");
		var stranger = await IdentitySeed.UserAsync(factory, otherTenant, Email(), "leitor");
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor");

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/users/{stranger}/roles/leitor");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
		var detail = await IdentitySeed.AdminClient(factory)
			.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{otherTenant}/users/{stranger}", Json);
		Strings(detail, "roles").Should().Equal("leitor");
	}

	[Fact]
	public async Task RemoveUserRole_NomeInvalido_Retorna400()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/users/{userId}/roles/nome%20invalido");

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}
```

Acrescentar a `ProfileAssignmentTokenTests`:

```csharp
	[Fact]
	public async Task Remover_PerfilSomeDoTokenDaRenovacao()
	{
		using var admin = await OperatorAsync();
		(await admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{_userId}/roles/inventario-admin", null))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);
		var session = await Driver.LoginAsync(_userEmail, UserScope);

		(await admin.DeleteAsync($"/api/v1/tenants/{_tenantId}/users/{_userId}/roles/inventario-admin"))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await RolesAfterRefreshAsync(await Driver.RefreshAsync(session.RefreshToken))).Should().NotContain("inventario-admin");
	}

	[Fact]
	public async Task Remover_ASiMesmoDoPerfilDeOperador_Retorna409()
	{
		using var admin = await OperatorAsync();

		var response = await admin.DeleteAsync(
			$"/api/v1/tenants/{SecureGatePlatform.TenantId}/users/{_operatorId}/roles/{SecureGatePlatform.OperatorRole}");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}
```

`tests/SecureGate/Secco.SecureGate.Tests/Integration/LastOperatorGuardTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Factory com banco PRÓPRIO: contar "operadores ativos" só é determinístico sem as outras classes
/// criando operadores no mesmo tenant de plataforma.
/// </summary>
public sealed class OperatorGuardSecureGateApiFactory : SecureGateApiFactory;

/// <summary>Collection isolada da guarda de último operador.</summary>
[CollectionDefinition(Name)]
public sealed class OperatorGuardApiCollectionDefinition : ICollectionFixture<OperatorGuardSecureGateApiFactory>
{
	/// <summary>Nome da collection.</summary>
	public const string Name = "SecureGate guarda de último operador";
}

/// <summary>
/// A instalação nunca fica sem operador ativo. Executado por client de MÁQUINA com
/// <c>securegate:admin</c> (o sub não é usuário): é o caso que a guarda de "não desativar a si
/// mesmo" não cobre.
/// </summary>
[Collection(OperatorGuardApiCollectionDefinition.Name)]
public class LastOperatorGuardTests(OperatorGuardSecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly string OperatorRoleUrl = SecureGatePlatform.OperatorRole;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();

		// Começa sem nenhum operador: cada teste monta exatamente o cenário que prova
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var normalized = SecureGatePlatform.OperatorRole.ToUpperInvariant();
		var roleId = await context.Roles
			.Where(r => r.TenantId == SecureGatePlatform.TenantId && r.NormalizedName == normalized)
			.Select(r => r.Id)
			.SingleAsync();
		context.UserRoles.RemoveRange(await context.UserRoles.Where(ur => ur.RoleId == roleId).ToListAsync());
		await context.SaveChangesAsync();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"op-guarda-{Guid.NewGuid():N}@secco.test";

	private Task<HttpResponseMessage> RemoveOperatorAsync(Guid userId) =>
		IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/users/{userId}/roles/{OperatorRoleUrl}");

	private Task<HttpResponseMessage> DeactivateAsync(Guid userId) =>
		IdentitySeed.AdminClient(factory)
			.PostAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/users/{userId}/deactivate", null);

	[Fact]
	public async Task Remover_UltimoOperadorAtivo_Retorna409()
	{
		var only = await IdentitySeed.PlatformOperatorAsync(factory, Email());

		(await RemoveOperatorAsync(only)).StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Desativar_UltimoOperadorAtivo_Retorna409()
	{
		var only = await IdentitySeed.PlatformOperatorAsync(factory, Email());

		(await DeactivateAsync(only)).StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Remover_ComOutroOperadorAtivo_Retorna204()
	{
		var first = await IdentitySeed.PlatformOperatorAsync(factory, Email());
		await IdentitySeed.PlatformOperatorAsync(factory, Email());

		(await RemoveOperatorAsync(first)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task Remover_QuandoOOutroEstaDesativado_Retorna409()
	{
		var active = await IdentitySeed.PlatformOperatorAsync(factory, Email());
		var inactive = await IdentitySeed.PlatformOperatorAsync(factory, Email());
		await IdentitySeed.DeactivateAsync(factory, inactive);

		// Operador desativado não opera: não conta como "outro ativo"
		(await RemoveOperatorAsync(active)).StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Remover_OperadorDesativado_ComUmAtivo_Retorna204()
	{
		await IdentitySeed.PlatformOperatorAsync(factory, Email());
		var inactive = await IdentitySeed.PlatformOperatorAsync(factory, Email());
		await IdentitySeed.DeactivateAsync(factory, inactive);

		(await RemoveOperatorAsync(inactive)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task Desativar_ComOutroOperadorAtivo_Retorna204()
	{
		var first = await IdentitySeed.PlatformOperatorAsync(factory, Email());
		await IdentitySeed.PlatformOperatorAsync(factory, Email());

		(await DeactivateAsync(first)).StatusCode.Should().Be(HttpStatusCode.NoContent);
	}
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~UserProfileManagementTests.RemoveUserRole|FullyQualifiedName~ProfileAssignmentTokenTests|FullyQualifiedName~LastOperatorGuardTests"`
Expected: FAIL — `DELETE` de perfil responde 405; `Desativar_UltimoOperadorAtivo_Retorna409` responde 204.

- [ ] **Step 3: Implementar**

Em `IUserDirectory.cs`, na interface:

```csharp
	/// <summary>Retira o usuário do perfil, ambos no tenant. Idempotente.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="roleName">Nome do perfil já validado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<RoleAssignmentOutcome> RemoveRoleAsync(Guid tenantId, Guid userId, string roleName, CancellationToken cancellationToken = default);

	/// <summary>Indica se o usuário do tenant é membro do perfil do MESMO tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="roleName">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> HasRoleAsync(Guid tenantId, Guid userId, string roleName, CancellationToken cancellationToken = default);

	/// <summary>
	/// Conta operadores de instalação ATIVOS (não desativados nem bloqueados), exceto o informado.
	/// </summary>
	/// <param name="excludingUserId">Usuário alvo da operação, fora da contagem.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<int> CountActiveOperatorsAsync(Guid excludingUserId, CancellationToken cancellationToken = default);
```

`src/SecureGate/Secco.SecureGate.Application/Users/OperatorGuard.cs`:

```csharp
namespace Secco.SecureGate.Application.Users;

/// <summary>
/// A instalação nunca fica sem operador ativo: reativar ou reatribuir exige token de operador, então
/// zerar os operadores só se desfaz com acesso direto ao banco.
/// </summary>
/// <remarks>
/// Duas operações simultâneas sobre os dois últimos operadores podem, cada uma, ver a outra ainda
/// ativa. Risco residual aceito: exige dois admins agindo ao mesmo tempo sobre os últimos operadores.
/// </remarks>
internal static class OperatorGuard
{
	/// <summary>Indica se tirar o usuário da condição de operador ativo deixaria a instalação sem nenhum.</summary>
	/// <param name="directory">Diretório de usuários.</param>
	/// <param name="userId">Usuário alvo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<bool> WouldLeaveNoActiveOperatorAsync(
		IUserDirectory directory, Guid userId, CancellationToken cancellationToken) =>
		await directory.HasRoleAsync(SecureGatePlatform.TenantId, userId, SecureGatePlatform.OperatorRole, cancellationToken)
			.ConfigureAwait(false)
		&& await directory.CountActiveOperatorsAsync(userId, cancellationToken).ConfigureAwait(false) == 0;
}
```

`src/SecureGate/Secco.SecureGate.Application/Users/RemoveUserRoleHandler.cs`:

```csharp
using Secco.SecureGate.Application.Roles;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>Comando de remoção de perfil.</summary>
/// <param name="TenantId">Tenant da rota.</param>
/// <param name="UserId">Usuário alvo.</param>
/// <param name="RoleName">Nome do perfil (rota).</param>
/// <param name="CallerSubject">O <c>sub</c> de quem chama — nunca vindo do corpo da requisição.</param>
public sealed record RemoveUserRoleCommand(Guid TenantId, Guid UserId, string? RoleName, string? CallerSubject);

/// <summary>
/// Retira um usuário de um perfil (issue #26). Idempotente. No perfil de operador da instalação,
/// ninguém se remove e o último operador ativo não sai.
/// </summary>
public sealed class RemoveUserRoleHandler(IUserDirectory userDirectory)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(RemoveUserRoleCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var name = command.RoleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return Result.Failure(SecureGateErrors.Roles.NameInvalid);
		}

		if (command.TenantId == SecureGatePlatform.TenantId
			&& string.Equals(name, SecureGatePlatform.OperatorRole, StringComparison.OrdinalIgnoreCase))
		{
			if (string.Equals(command.CallerSubject, command.UserId.ToString(), StringComparison.OrdinalIgnoreCase))
			{
				return Result.Failure(SecureGateErrors.Users.CannotRemoveSelfFromOperator);
			}

			if (await OperatorGuard.WouldLeaveNoActiveOperatorAsync(userDirectory, command.UserId, cancellationToken)
				.ConfigureAwait(false))
			{
				return Result.Failure(SecureGateErrors.Users.LastActiveOperator);
			}
		}

		return AddUserRoleHandler.ToResult(await userDirectory
			.RemoveRoleAsync(command.TenantId, command.UserId, name, cancellationToken)
			.ConfigureAwait(false));
	}
}
```

Em `SetUserActivationHandler.HandleAsync`, logo depois do bloco que devolve `CannotDeactivateSelf`:

```csharp
		// Vale também para client de máquina com securegate:admin, que não é operador e por isso não é
		// barrado pela regra de desativar a si mesmo
		if (!command.Active
			&& command.TenantId == SecureGatePlatform.TenantId
			&& await OperatorGuard.WouldLeaveNoActiveOperatorAsync(userDirectory, command.UserId, cancellationToken)
				.ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.Users.LastActiveOperator);
		}
```

Em `SecureGateApplicationExtensions.cs`: `services.AddScoped<RemoveUserRoleHandler>();`.

Em `UserAccountService.cs`:

```csharp
	public async Task<RoleAssignmentOutcome> RemoveRoleAsync(
		Guid tenantId, Guid userId, string roleName, CancellationToken cancellationToken = default)
	{
		if (!await BelongsToTenantAsync(tenantId, userId, cancellationToken).ConfigureAwait(false))
		{
			return RoleAssignmentOutcome.UserNotFound;
		}

		var roleId = await FindRoleIdAsync(tenantId, roleName, cancellationToken).ConfigureAwait(false);

		if (roleId is null)
		{
			return RoleAssignmentOutcome.RoleNotFound;
		}

		var assignment = await context.UserRoles
			.FirstOrDefaultAsync(userRole => userRole.UserId == userId && userRole.RoleId == roleId, cancellationToken)
			.ConfigureAwait(false);

		if (assignment is not null)
		{
			context.UserRoles.Remove(assignment);
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return RoleAssignmentOutcome.Done;
	}

	public Task<bool> HasRoleAsync(Guid tenantId, Guid userId, string roleName, CancellationToken cancellationToken = default)
	{
		var normalized = roleName.ToUpperInvariant();

		return (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			join user in context.Users.AsNoTracking() on userRole.UserId equals user.Id
			where userRole.UserId == userId
				&& user.TenantId == tenantId
				&& role.TenantId == tenantId
				&& role.NormalizedName == normalized
			select userRole.UserId)
			.AnyAsync(cancellationToken);
	}

	public Task<int> CountActiveOperatorsAsync(Guid excludingUserId, CancellationToken cancellationToken = default)
	{
		var normalized = SecureGatePlatform.OperatorRole.ToUpperInvariant();
		var now = DateTimeOffset.UtcNow;

		return (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			join user in context.Users.AsNoTracking() on userRole.UserId equals user.Id
			where role.TenantId == SecureGatePlatform.TenantId
				&& role.NormalizedName == normalized
				&& user.TenantId == SecureGatePlatform.TenantId
				&& user.Id != excludingUserId
				&& (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= now)
			select user.Id)
			.Distinct()
			.CountAsync(cancellationToken);
	}
```

Em `UserEndpoints.cs`, antes de `return endpoints;`:

```csharp
		group.MapDelete("/{userId:guid}/roles/{role}", async (
				Guid tenantId,
				Guid userId,
				string role,
				ClaimsPrincipal caller,
				RemoveUserRoleHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new RemoveUserRoleCommand(tenantId, userId, role, caller.FindFirst(SeccoClaims.Subject)?.Value),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("RemoveUserRole")
			.WithSummary("Retira o usuário do perfil (idempotente). Vale no token na próxima renovação.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);
```

O `DeactivateUser` já documenta 409 desde a 0.6.0; nada a mudar nele.

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~UserProfileManagementTests|FullyQualifiedName~ProfileAssignmentTokenTests|FullyQualifiedName~LastOperatorGuardTests|FullyQualifiedName~SessionRevocationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate tests/SecureGate
git commit -m "feat(securegate): remover perfil com guardas do operador da instalação (#26)

Ninguém se remove do perfil de operador, e nem a remoção nem a desativação
podem deixar a instalação sem operador ativo — inclusive quando quem age é
um client de máquina com securegate:admin.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: 401/403 em todos os endpoints novos e guarda das APIs do Identity

**Files:**
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Integration/RoleProfileManagementTests.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/IdentityRoleApiGuardTests.cs`

**Interfaces:**
- Consumes: rotas das Tasks 3–7.

- [ ] **Step 1: Teoria de autenticação e autorização**

Acrescentar a `RoleProfileManagementTests` (using `System.Net.Http.Headers`):

```csharp
	public static TheoryData<string, string> NovasRotas() => new()
	{
		{ "GET", "/api/v1/tenants/{0}/roles/leitor" },
		{ "DELETE", "/api/v1/tenants/{0}/roles/leitor" },
		{ "GET", "/api/v1/tenants/{0}/roles/leitor/members" },
		{ "GET", "/api/v1/tenants/{0}/users/{1}" },
		{ "POST", "/api/v1/tenants/{0}/users/{1}/roles/leitor" },
		{ "DELETE", "/api/v1/tenants/{0}/users/{1}/roles/leitor" },
	};

	[Theory]
	[MemberData(nameof(NovasRotas))]
	public async Task NovasRotas_SemToken_Retornam401(string method, string template)
	{
		var request = new HttpRequestMessage(new HttpMethod(method), string.Format(template, _tenantId, Guid.CreateVersion7()));

		(await factory.CreateClient().SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Theory]
	[MemberData(nameof(NovasRotas))]
	public async Task NovasRotas_SemScopeAdmin_Retornam403(string method, string template)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes("logstream"));
		var request = new HttpRequestMessage(new HttpMethod(method), string.Format(template, _tenantId, Guid.CreateVersion7()));

		(await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}
```

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~RoleProfileManagementTests.NovasRotas"`
Expected: PASS, 12 casos (os grupos já exigem o scope; o teste trava a regressão).

- [ ] **Step 2: Escrever a guarda de código**

`tests/SecureGate/Secco.SecureGate.Tests/Unit/IdentityRoleApiGuardTests.cs`:

```csharp
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// As APIs de role do Identity localizam perfil por nome GLOBAL. Aqui o nome só é único por tenant:
/// usá-las alcançaria o perfil homônimo de outro tenant. O teste lê o código do SecureGate e falha ao
/// encontrá-las — sem biblioteca de arquitetura nova.
/// </summary>
public partial class IdentityRoleApiGuardTests
{
	[GeneratedRegex(@"\b(AddToRolesAsync|AddToRoleAsync|RemoveFromRolesAsync|RemoveFromRoleAsync|IsInRoleAsync|GetUsersInRoleAsync)\s*\(|\bRoleManager\s*<")]
	private static partial Regex ForbiddenCall();

	[Fact]
	public void SecureGate_NaoUsaApisDeRoleQueIgnoramOTenant()
	{
		var root = FindRepositoryRoot();

		var offenders = Directory
			.EnumerateFiles(Path.Combine(root, "src", "SecureGate"), "*.cs", SearchOption.AllDirectories)
			.Where(path => !IsGenerated(path))
			.SelectMany(path => File.ReadLines(path).Select((line, index) => (Path: path, Line: line, Number: index + 1)))
			.Where(entry => ForbiddenCall().IsMatch(entry.Line))
			.Select(entry => $"{Path.GetRelativePath(root, entry.Path)}:{entry.Number}")
			.ToList();

		offenders.Should().BeEmpty("perfil é localizado sempre por (tenant, nome normalizado)");
	}

	[Theory]
	[InlineData("await userManager.AddToRoleAsync(user, \"admin\");")]
	[InlineData("await userManager.RemoveFromRolesAsync(user, roles);")]
	[InlineData("if (await userManager.IsInRoleAsync (user, name))")]
	[InlineData("RoleManager<Role> roleManager")]
	public void Guarda_ReconheceChamadasProibidas(string line) => ForbiddenCall().IsMatch(line).Should().BeTrue();

	[Theory]
	[InlineData("/// não usa <c>UserManager.AddToRoleAsync</c> por nome")]
	[InlineData("var roles = await userManager.GetRolesAsync(user);")]
	public void Guarda_NaoAcusaComentarioNemLeituraPorUsuario(string line) => ForbiddenCall().IsMatch(line).Should().BeFalse();

	private static bool IsGenerated(string path)
	{
		var separator = Path.DirectorySeparatorChar;

		return path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
			|| path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
			|| path.Contains($"{separator}Migrations", StringComparison.Ordinal);
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);

		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Secco.Platform.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName
			?? throw new InvalidOperationException("Raiz do repositório (Secco.Platform.slnx) não encontrada.");
	}
}
```

- [ ] **Step 3: Rodar e ver passar; provar que morde**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~IdentityRoleApiGuardTests"`
Expected: PASS, 7 casos.

Prova: acrescentar temporariamente em `UserAccountService.cs` a linha `// teste: userManager.AddToRoleAsync(null!, "x");`, rodar de novo e ver `SecureGate_NaoUsaApisDeRoleQueIgnoramOTenant` FALHAR citando o arquivo e a linha; **remover a linha** e confirmar com `git diff --stat src/SecureGate/Secco.SecureGate.Infrastructure/Users/UserAccountService.cs` que nada sobrou.

- [ ] **Step 4: Commit**

```bash
git add tests/SecureGate
git commit -m "test(securegate): perímetro dos endpoints de perfil e guarda das APIs de role

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 9: Contrato, client e serviços do AdminPortal

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Api/openapi/openapi.json` (regenerado)
- Modify: `src/AdminPortal/Secco.AdminPortal/Services/AdminModels.cs`
- Modify: `src/AdminPortal/Secco.AdminPortal/Services/IUserAdminService.cs`
- Modify: `src/AdminPortal/Secco.AdminPortal/Services/IRoleAdminService.cs`
- Test: `tests/AdminPortal/Secco.AdminPortal.Tests/IdentityAdminServicesTests.cs`

**Interfaces:**
- Consumes: rotas `GetRole`, `DeleteRole`, `ListRoleMembers`, `GetUser`, `AddUserRole`, `RemoveUserRole`, `DeactivateUser`, `ActivateUser`. Métodos gerados pelo NSwag: `GetRoleAsync(Guid tenantId, string role)`, `DeleteRoleAsync(Guid tenantId, string role)`, `ListRoleMembersAsync(Guid tenantId, string role, int? page, int? size)`, `GetUserAsync(Guid tenantId, Guid userId)`, `AddUserRoleAsync(Guid tenantId, Guid userId, string role)`, `RemoveUserRoleAsync(Guid tenantId, Guid userId, string role)`, `DeactivateUserAsync(Guid tenantId, Guid userId)`, `ActivateUserAsync(Guid tenantId, Guid userId)` — todos com sobrecarga `CancellationToken`. Tipos: `RoleDetailDto`, `RoleMemberDto`, `PagedResultOfRoleMemberDto`, `UserDetailDto`; `UserDto.Status`.
- Produces: `UserSummary(Guid Id, string Email, IReadOnlyList<string> Roles, string Status)`; `UserDetail(Guid Id, string Email, string Status, DateTimeOffset? LockoutEnd, IReadOnlyList<string> Roles, IReadOnlyList<string> EffectivePermissions, IReadOnlyList<string> ExternalLogins)`; `RoleDetail(string Name, IReadOnlyList<string> Permissions, bool IsReserved, int MemberCount)`; `RoleMemberSummary(Guid UserId, string Email, string Status)`; `MemberPage(IReadOnlyList<RoleMemberSummary> Items, int Page, int TotalPages, long TotalCount)`; `UserStatusText.Describe(string status) : string`; `IUserAdminService.GetUserAsync`, `AddRoleAsync`, `RemoveRoleAsync`, `SetActiveAsync`; `IRoleAdminService.GetRoleAsync`, `DeleteRoleAsync`, `ListMembersAsync(Guid tenantId, string role, int page, CancellationToken)`, `IRoleAdminService.MembersPageSize = 20`.

- [ ] **Step 1: Regenerar o contrato**

Run (Git Bash): `SECCO_UPDATE_OPENAPI=true dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~OpenApiContractTests"`
Depois: `git diff -- src/SecureGate/Secco.SecureGate.Api/openapi/openapi.json | grep "^-" | grep -v "^---"`
Expected: nenhuma linha removida além de ajustes de `summary`; operações e schemas só acrescentados. Qualquer remoção de operação ou propriedade é quebra de contrato — parar e investigar.

Run: `dotnet build src/AdminPortal/Secco.AdminPortal/Secco.AdminPortal.csproj -c Release`
Expected: build OK (o client é gerado no build a partir do `openapi.json`).

- [ ] **Step 2: Escrever os testes que falham**

Acrescentar a `IdentityAdminServicesTests`:

```csharp
	[Fact]
	public async Task ListUsers_ProjetaSituacao()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		client.ListUsersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<UserDto>
		{
			new() { Id = Guid.NewGuid(), Email = "ana@acme.test", TenantId = tenantId, Roles = [], Status = "Deactivated" },
		});

		var users = await new SecureGateUserAdminService(factory).ListUsersAsync(tenantId);

		users[0].Status.Should().Be("Deactivated");
	}

	[Fact]
	public async Task GetUser_ProjetaDetalhe()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		client.GetUserAsync(tenantId, userId, Arg.Any<CancellationToken>()).Returns(new UserDetailDto
		{
			Id = userId,
			Email = "ana@acme.test",
			TenantId = tenantId,
			Status = "Active",
			Roles = ["leitor"],
			EffectivePermissions = ["documentos:read"],
			ExternalLogins = ["EntraId"],
		});

		var user = await new SecureGateUserAdminService(factory).GetUserAsync(tenantId, userId);

		user.Email.Should().Be("ana@acme.test");
		user.Roles.Should().Equal("leitor");
		user.EffectivePermissions.Should().Equal("documentos:read");
		user.ExternalLogins.Should().Equal("EntraId");
	}

	[Fact]
	public async Task AddERemoveRole_ChamamOClient()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		var service = new SecureGateUserAdminService(factory);

		await service.AddRoleAsync(tenantId, userId, "leitor");
		await service.RemoveRoleAsync(tenantId, userId, "leitor");

		await client.Received(1).AddUserRoleAsync(tenantId, userId, "leitor", Arg.Any<CancellationToken>());
		await client.Received(1).RemoveUserRoleAsync(tenantId, userId, "leitor", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SetActive_EscolheAOperacao()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		var service = new SecureGateUserAdminService(factory);

		await service.SetActiveAsync(tenantId, userId, active: false);
		await service.SetActiveAsync(tenantId, userId, active: true);

		await client.Received(1).DeactivateUserAsync(tenantId, userId, Arg.Any<CancellationToken>());
		await client.Received(1).ActivateUserAsync(tenantId, userId, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetRole_ProjetaDetalhe()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		client.GetRoleAsync(tenantId, "leitor", Arg.Any<CancellationToken>()).Returns(new RoleDetailDto
		{
			Name = "leitor",
			Permissions = ["documentos:read"],
			IsReserved = false,
			MemberCount = 3,
		});

		var role = await new SecureGateRoleAdminService(factory).GetRoleAsync(tenantId, "leitor");

		role.Should().BeEquivalentTo(new RoleDetail("leitor", ["documentos:read"], false, 3));
	}

	[Fact]
	public async Task ListMembers_PedeAPaginaComTamanhoFixo()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var memberId = Guid.NewGuid();
		client.ListRoleMembersAsync(tenantId, "leitor", 2, IRoleAdminService.MembersPageSize, Arg.Any<CancellationToken>())
			.Returns(new PagedResultOfRoleMemberDto
			{
				Items = [new RoleMemberDto { UserId = memberId, Email = "ana@acme.test", Status = "Active" }],
				Page = 2,
				Size = IRoleAdminService.MembersPageSize,
				TotalCount = 21,
				TotalPages = 2,
			});

		var page = await new SecureGateRoleAdminService(factory).ListMembersAsync(tenantId, "leitor", 2);

		page.Page.Should().Be(2);
		page.TotalPages.Should().Be(2);
		page.Items.Should().ContainSingle(m => m.UserId == memberId && m.Status == "Active");
	}

	[Fact]
	public async Task DeleteRole_ChamaOClient()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();

		await new SecureGateRoleAdminService(factory).DeleteRoleAsync(tenantId, "leitor");

		await client.Received(1).DeleteRoleAsync(tenantId, "leitor", Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData("Active", "Ativo")]
	[InlineData("Deactivated", "Desativado")]
	[InlineData("LockedOut", "Bloqueado")]
	[InlineData("Outro", "Outro")]
	public void UserStatusText_Traduz(string status, string expected) =>
		UserStatusText.Describe(status).Should().Be(expected);

	[Fact]
	public void ApiErrorFormatter_Conflito_MostraODetalheDoServidor() =>
		ApiErrorFormatter.Describe(409, """{"title":"Conflict","detail":"A operação deixaria a instalação sem nenhum operador ativo."}""")
			.Should().Be("A operação deixaria a instalação sem nenhum operador ativo.");
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/AdminPortal/Secco.AdminPortal.Tests/Secco.AdminPortal.Tests.csproj -c Release --filter "FullyQualifiedName~IdentityAdminServicesTests"`
Expected: erro de compilação — métodos e modelos novos não existem.

- [ ] **Step 4: Implementar**

`AdminModels.cs` — substituir `UserSummary` e `RoleSummary` e acrescentar os demais (manter `TenantDetail`):

```csharp
/// <summary>Usuário na listagem do tenant.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Roles">Perfis.</param>
/// <param name="Status">Situação (Active, Deactivated, LockedOut).</param>
public sealed record UserSummary(Guid Id, string Email, IReadOnlyList<string> Roles, string Status);

/// <summary>Usuário detalhado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Status">Situação.</param>
/// <param name="LockoutEnd">Fim do bloqueio por tentativas.</param>
/// <param name="Roles">Perfis.</param>
/// <param name="EffectivePermissions">Permissões efetivas.</param>
/// <param name="ExternalLogins">Provedores externos vinculados.</param>
public sealed record UserDetail(
	Guid Id,
	string Email,
	string Status,
	DateTimeOffset? LockoutEnd,
	IReadOnlyList<string> Roles,
	IReadOnlyList<string> EffectivePermissions,
	IReadOnlyList<string> ExternalLogins);

/// <summary>Perfil na listagem do tenant.</summary>
/// <param name="Name">Nome.</param>
/// <param name="Permissions">Permissões gravadas.</param>
public sealed record RoleSummary(string Name, IReadOnlyList<string> Permissions);

/// <summary>Perfil detalhado.</summary>
/// <param name="Name">Nome.</param>
/// <param name="Permissions">Permissões efetivas.</param>
/// <param name="IsReserved">Reservado da plataforma.</param>
/// <param name="MemberCount">Quantidade de membros.</param>
public sealed record RoleDetail(string Name, IReadOnlyList<string> Permissions, bool IsReserved, int MemberCount);

/// <summary>Membro de perfil.</summary>
/// <param name="UserId">Usuário.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Status">Situação.</param>
public sealed record RoleMemberSummary(Guid UserId, string Email, string Status);

/// <summary>Página de membros.</summary>
/// <param name="Items">Membros.</param>
/// <param name="Page">Página atual.</param>
/// <param name="TotalPages">Total de páginas.</param>
/// <param name="TotalCount">Total de membros.</param>
public sealed record MemberPage(IReadOnlyList<RoleMemberSummary> Items, int Page, int TotalPages, long TotalCount);

/// <summary>Texto da situação da conta para a tela.</summary>
public static class UserStatusText
{
	/// <summary>Traduz a situação vinda do SecureGate; valor desconhecido aparece como veio.</summary>
	/// <param name="status">Situação.</param>
	public static string Describe(string status) => status switch
	{
		"Active" => "Ativo",
		"Deactivated" => "Desativado",
		"LockedOut" => "Bloqueado",
		_ => status,
	};
}
```

`IUserAdminService.cs` — a interface ganha:

```csharp
	Task<UserDetail> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	Task AddRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken = default);

	Task RemoveRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken = default);

	Task SetActiveAsync(Guid tenantId, Guid userId, bool active, CancellationToken cancellationToken = default);
```

(com `/// <summary>` em cada membro, no padrão do arquivo). Em `SecureGateUserAdminService`, a projeção de `ListUsersAsync` passa a `new UserSummary(user.Id, user.Email, [.. user.Roles], user.Status)` e entram:

```csharp
	public async Task<UserDetail> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var user = await client.GetUserAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);

		return new UserDetail(
			user.Id,
			user.Email,
			user.Status,
			user.LockoutEnd,
			[.. user.Roles],
			[.. user.EffectivePermissions],
			[.. user.ExternalLogins]);
	}

	public async Task AddRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.AddUserRoleAsync(tenantId, userId, role, cancellationToken).ConfigureAwait(false);
	}

	public async Task RemoveRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.RemoveUserRoleAsync(tenantId, userId, role, cancellationToken).ConfigureAwait(false);
	}

	public async Task SetActiveAsync(Guid tenantId, Guid userId, bool active, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		if (active)
		{
			await client.ActivateUserAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			await client.DeactivateUserAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
		}
	}
```

`IRoleAdminService.cs` — a interface ganha:

```csharp
	/// <summary>Tamanho fixo da página de membros na tela.</summary>
	const int MembersPageSize = 20;

	Task<RoleDetail> GetRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken = default);

	Task DeleteRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken = default);

	Task<MemberPage> ListMembersAsync(Guid tenantId, string role, int page, CancellationToken cancellationToken = default);
```

e `SecureGateRoleAdminService`:

```csharp
	public async Task<RoleDetail> GetRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var detail = await client.GetRoleAsync(tenantId, role, cancellationToken).ConfigureAwait(false);

		return new RoleDetail(detail.Name, [.. detail.Permissions], detail.IsReserved, detail.MemberCount);
	}

	public async Task DeleteRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.DeleteRoleAsync(tenantId, role, cancellationToken).ConfigureAwait(false);
	}

	public async Task<MemberPage> ListMembersAsync(Guid tenantId, string role, int page, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var result = await client
			.ListRoleMembersAsync(tenantId, role, page, IRoleAdminService.MembersPageSize, cancellationToken)
			.ConfigureAwait(false);

		return new MemberPage(
			[.. result.Items.Select(member => new RoleMemberSummary(member.UserId, member.Email, member.Status))],
			result.Page,
			result.TotalPages,
			result.TotalCount);
	}
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/AdminPortal/Secco.AdminPortal.Tests/Secco.AdminPortal.Tests.csproj -c Release`
Expected: PASS. Se `TenantManagement.razor` quebrar a compilação por causa do `UserSummary` novo, a Task 10 o reescreve — neste passo, ajustar só o necessário para compilar (nenhum, se o razor não construir `UserSummary`).

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~OpenApiContractTests"`
Expected: PASS (snapshot atualizado).

- [ ] **Step 6: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Api/openapi src/AdminPortal tests/AdminPortal
git commit -m "feat(adminportal): serviços de perfis e detalhe de usuário sobre o client regenerado (#26)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 10: Telas de perfis e usuário no AdminPortal

**Files:**
- Modify (reescrever): `src/AdminPortal/Secco.AdminPortal/Components/Pages/TenantManagement.razor`
- Create: `src/AdminPortal/Secco.AdminPortal/Components/Pages/RoleManagement.razor`
- Create: `src/AdminPortal/Secco.AdminPortal/Components/Pages/UserManagement.razor`

**Interfaces:**
- Consumes: serviços e modelos da Task 9; `ApiErrorFormatter`; `AdminPortalDefaults.OperatorPolicy`.

Os nomes das páginas não podem ser `RoleDetail`/`UserDetail`: a classe do componente sombrearia os modelos de mesmo nome.

- [ ] **Step 1: Reescrever a página do tenant**

`TenantManagement.razor` — substituir as seções **Usuários** e **Roles & permissões** e o `@code` correspondente. A seção **Bancos de tenant** e `UpsertDatabaseAsync` permanecem idênticas. Conteúdo completo:

```razor
@page "/tenants/{TenantId:guid}"
@attribute [Authorize(Policy = AdminPortalDefaults.OperatorPolicy)]
@inject ITenantAdminService TenantAdminService
@inject IUserAdminService UserAdminService
@inject IRoleAdminService RoleAdminService
@inject ILogger<TenantManagement> Logger
@using Secco.SecureGate.Client

<PageTitle>@(_tenant?.Name ?? "Tenant") — Secco AdminPortal</PageTitle>

<p><a href="tenants">&larr; Tenants</a></p>

@if (_loadError is not null)
{
    <p class="error" role="alert">@_loadError</p>
}
else if (_tenant is null)
{
    <p>Carregando…</p>
}
else
{
    <h1>@_tenant.Name</h1>
    <p class="muted">@_tenant.Slug · @(_tenant.IsActive ? "Ativo" : "Inativo") · <a href="@($"tenants/{TenantId}/logs")">Ver logs</a></p>

    @if (_message is not null)
    {
        <p class="success" role="status">@_message</p>
    }
    @if (_actionError is not null)
    {
        <p class="error" role="alert">@_actionError</p>
    }

    <section>
        <h2>Usuários</h2>
        @if (_users is null)
        {
            <p>Carregando…</p>
        }
        else if (_users.Count == 0)
        {
            <p>Nenhum usuário cadastrado.</p>
        }
        else
        {
            <table class="grid">
                <thead><tr><th>E-mail</th><th>Situação</th><th>Perfis</th></tr></thead>
                <tbody>
                    @foreach (var user in _users)
                    {
                        <tr>
                            <td><a href="@($"tenants/{TenantId}/users/{user.Id}")">@user.Email</a></td>
                            <td>@UserStatusText.Describe(user.Status)</td>
                            <td>@(user.Roles.Count == 0 ? "—" : string.Join(", ", user.Roles))</td>
                        </tr>
                    }
                </tbody>
            </table>
        }

        <div class="form-card">
            <h3>Novo usuário</h3>
            <label>E-mail<input type="email" @bind="_newUserEmail" autocomplete="off" /></label>
            <label>Senha inicial<input type="password" @bind="_newUserPassword" autocomplete="new-password" /></label>
            @if (_roles is { Count: > 0 })
            {
                <fieldset>
                    <legend>Perfis</legend>
                    @foreach (var role in _roles)
                    {
                        <label>
                            <input type="checkbox"
                                   checked="@_newUserRoles.Contains(role.Name)"
                                   @onchange="e => ToggleNewUserRole(role.Name, e)" />
                            @role.Name
                        </label>
                    }
                </fieldset>
            }
            <button @onclick="CreateUserAsync" disabled="@_busy">Criar usuário</button>
        </div>
    </section>

    <section>
        <h2>Perfis</h2>
        @if (_roles is null)
        {
            <p>Carregando…</p>
        }
        else if (_roles.Count == 0)
        {
            <p>Nenhum perfil cadastrado.</p>
        }
        else
        {
            <table class="grid">
                <thead><tr><th>Perfil</th><th>Permissões</th></tr></thead>
                <tbody>
                    @foreach (var role in _roles)
                    {
                        <tr>
                            <td><a href="@($"tenants/{TenantId}/roles/{Uri.EscapeDataString(role.Name)}")">@role.Name</a></td>
                            <td>@role.Permissions.Count</td>
                        </tr>
                    }
                </tbody>
            </table>
        }

        <div class="form-card">
            <h3>Novo perfil</h3>
            <label>Nome<input type="text" @bind="_newRoleName" placeholder="ex.: financeiro-user" /></label>
            <button @onclick="CreateRoleAsync" disabled="@_busy">Criar perfil</button>
        </div>
    </section>

    <section>
        <h2>Bancos de tenant</h2>
        @if (_tenant.Products.Count == 0)
        {
            <p>Nenhum banco cadastrado.</p>
        }
        else
        {
            <table class="grid">
                <thead><tr><th>Produto</th><th>Banco</th></tr></thead>
                <tbody>
                    @foreach (var product in _tenant.Products)
                    {
                        <tr>
                            <td>@product</td>
                            <td class="muted">cadastrado</td>
                        </tr>
                    }
                </tbody>
            </table>
        }

        <div class="form-card">
            <h3>Cadastrar / rotacionar banco</h3>
            <label>Produto<input type="text" @bind="_newDbProduct" placeholder="ex.: logstream" /></label>
            <label>Connection string (write-only)<input type="password" @bind="_newDbConnectionString" autocomplete="off" /></label>
            <button @onclick="UpsertDatabaseAsync" disabled="@_busy">Salvar banco</button>
        </div>
    </section>
}

@code {
    /// <summary>Tenant alvo (rota).</summary>
    [Parameter] public Guid TenantId { get; set; }

    private TenantDetail? _tenant;
    private IReadOnlyList<UserSummary>? _users;
    private IReadOnlyList<RoleSummary>? _roles;

    private string? _loadError;
    private string? _actionError;
    private string? _message;
    private bool _busy;

    private string _newUserEmail = string.Empty;
    private string _newUserPassword = string.Empty;
    private readonly HashSet<string> _newUserRoles = new(StringComparer.Ordinal);
    private string _newRoleName = string.Empty;
    private string _newDbProduct = string.Empty;
    private string _newDbConnectionString = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _tenant = await TenantAdminService.GetTenantAsync(TenantId);
            await ReloadUsersAsync();
            await ReloadRolesAsync();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Falha ao carregar o tenant {TenantId}.", TenantId);
            _loadError = "Não foi possível carregar o tenant.";
        }
    }

    private async Task ReloadUsersAsync() => _users = await UserAdminService.ListUsersAsync(TenantId);

    private async Task ReloadRolesAsync() => _roles = await RoleAdminService.ListRolesAsync(TenantId);

    private void ToggleNewUserRole(string role, ChangeEventArgs change)
    {
        if (change.Value is true)
        {
            _newUserRoles.Add(role);
        }
        else
        {
            _newUserRoles.Remove(role);
        }
    }

    private async Task CreateUserAsync()
    {
        if (string.IsNullOrWhiteSpace(_newUserEmail) || string.IsNullOrWhiteSpace(_newUserPassword))
        {
            SetError("Informe e-mail e senha.");
            return;
        }

        await RunAsync(async () =>
        {
            await UserAdminService.CreateUserAsync(TenantId, _newUserEmail.Trim(), _newUserPassword, [.. _newUserRoles]);
            _newUserEmail = _newUserPassword = string.Empty;
            _newUserRoles.Clear();
            await ReloadUsersAsync();
            _message = "Usuário criado.";
        });
    }

    private async Task CreateRoleAsync()
    {
        if (string.IsNullOrWhiteSpace(_newRoleName))
        {
            SetError("Informe o nome do perfil.");
            return;
        }

        await RunAsync(async () =>
        {
            await RoleAdminService.CreateRoleAsync(TenantId, _newRoleName.Trim());
            _newRoleName = string.Empty;
            await ReloadRolesAsync();
            _message = "Perfil criado.";
        });
    }

    private async Task UpsertDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(_newDbProduct) || string.IsNullOrWhiteSpace(_newDbConnectionString))
        {
            SetError("Informe o produto e a connection string.");
            return;
        }

        await RunAsync(async () =>
        {
            await TenantAdminService.UpsertDatabaseAsync(
                TenantId, _newDbProduct.Trim().ToLowerInvariant(), _newDbConnectionString);
            _newDbProduct = _newDbConnectionString = string.Empty;
            _tenant = await TenantAdminService.GetTenantAsync(TenantId); // atualiza os Products
            _message = "Banco salvo.";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        _actionError = _message = null;
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            _actionError = ApiErrorFormatter.Describe(exception.StatusCode, exception.Response);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Falha na operação de gestão do tenant {TenantId}.", TenantId);
            _actionError = "Falha inesperada. Tente novamente.";
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetError(string message)
    {
        _actionError = message;
        _message = null;
    }
}
```

- [ ] **Step 2: Criar a página do perfil**

`src/AdminPortal/Secco.AdminPortal/Components/Pages/RoleManagement.razor`:

```razor
@page "/tenants/{TenantId:guid}/roles/{RoleName}"
@attribute [Authorize(Policy = AdminPortalDefaults.OperatorPolicy)]
@inject IRoleAdminService RoleAdminService
@inject IUserAdminService UserAdminService
@inject NavigationManager Navigation
@inject ILogger<RoleManagement> Logger
@using Secco.SecureGate.Client

<PageTitle>Perfil @RoleName — Secco AdminPortal</PageTitle>

<p><a href="@($"tenants/{TenantId}")">&larr; Tenant</a></p>

@if (_loadError is not null)
{
    <p class="error" role="alert">@_loadError</p>
}
else if (_role is null || _members is null)
{
    <p>Carregando…</p>
}
else
{
    <h1>Perfil @_role.Name</h1>
    <p class="muted">@(_role.IsReserved ? "Reservado da plataforma · " : string.Empty)@_role.MemberCount membro(s)</p>

    @if (_message is not null)
    {
        <p class="success" role="status">@_message</p>
    }
    @if (_actionError is not null)
    {
        <p class="error" role="alert">@_actionError</p>
    }
    @if (_pending is not null)
    {
        <div class="form-card" role="alertdialog">
            <p>@_pending.Message</p>
            <button @onclick="ConfirmAsync" disabled="@_busy">Confirmar</button>
            <button @onclick="() => _pending = null" disabled="@_busy">Cancelar</button>
        </div>
    }

    <section>
        <h2>Permissões</h2>
        @if (_role.IsReserved)
        {
            <p class="muted">Definidas pela plataforma; não editáveis.</p>
            <ul>
                @foreach (var permission in _role.Permissions)
                {
                    <li><code>@permission</code></li>
                }
            </ul>
        }
        else
        {
            <div class="form-card">
                <label>Permissões (uma <code>recurso:acao</code> por linha)
                    <textarea rows="6" @bind="_permissionsText"></textarea>
                </label>
                <button @onclick="SavePermissionsAsync" disabled="@_busy">Salvar permissões</button>
            </div>
        }
    </section>

    <section>
        <h2>Membros</h2>
        @if (_members.Items.Count == 0)
        {
            <p>Nenhum membro.</p>
        }
        else
        {
            <table class="grid">
                <thead><tr><th>E-mail</th><th>Situação</th><th></th></tr></thead>
                <tbody>
                    @foreach (var member in _members.Items)
                    {
                        <tr>
                            <td><a href="@($"tenants/{TenantId}/users/{member.UserId}")">@member.Email</a></td>
                            <td>@UserStatusText.Describe(member.Status)</td>
                            <td>
                                <button @onclick="() => AskRemoveMember(member)" disabled="@_busy">Remover</button>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
            <p>
                <button @onclick="() => GoToPageAsync(_members.Page - 1)" disabled="@(_busy || _members.Page <= 1)">Anterior</button>
                Página @_members.Page de @Math.Max(_members.TotalPages, 1)
                <button @onclick="() => GoToPageAsync(_members.Page + 1)" disabled="@(_busy || _members.Page >= _members.TotalPages)">Próxima</button>
            </p>
        }

        @if (Candidates.Count > 0)
        {
            <div class="form-card">
                <h3>Adicionar membro</h3>
                <label>Usuário
                    <select @bind="_userToAdd">
                        <option value="">Selecione…</option>
                        @foreach (var user in Candidates)
                        {
                            <option value="@user.Id">@user.Email</option>
                        }
                    </select>
                </label>
                <button @onclick="AddMemberAsync" disabled="@(_busy || string.IsNullOrEmpty(_userToAdd))">Adicionar</button>
            </div>
        }
    </section>

    @if (!_role.IsReserved)
    {
        <section>
            <h2>Excluir perfil</h2>
            @if (_role.MemberCount > 0)
            {
                <p class="muted">Remova os membros antes de excluir.</p>
            }
            <button @onclick="AskDeleteRole" disabled="@(_busy || _role.MemberCount > 0)">Excluir perfil</button>
        </section>
    }
}

@code {
    /// <summary>Tenant alvo (rota).</summary>
    [Parameter] public Guid TenantId { get; set; }

    /// <summary>Perfil alvo (rota).</summary>
    [Parameter] public string RoleName { get; set; } = string.Empty;

    private RoleDetail? _role;
    private MemberPage? _members;
    private IReadOnlyList<UserSummary> _users = [];
    private string _permissionsText = string.Empty;
    private string? _userToAdd;

    private string? _loadError;
    private string? _actionError;
    private string? _message;
    private bool _busy;
    private PendingAction? _pending;

    private sealed record PendingAction(string Message, Func<Task> Action);

    private IReadOnlyList<UserSummary> Candidates =>
        _role is null
            ? []
            : [.. _users.Where(user => !user.Roles.Contains(_role.Name, StringComparer.OrdinalIgnoreCase))];

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            await ReloadAsync(page: 1);
        }
        catch (ApiException exception) when (exception.StatusCode == 404)
        {
            _loadError = "Perfil não encontrado.";
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Falha ao carregar o perfil {Role} do tenant {TenantId}.", RoleName, TenantId);
            _loadError = "Não foi possível carregar o perfil.";
        }
    }

    private async Task ReloadAsync(int page)
    {
        _role = await RoleAdminService.GetRoleAsync(TenantId, RoleName);
        _permissionsText = string.Join('\n', _role.Permissions);
        _members = await RoleAdminService.ListMembersAsync(TenantId, RoleName, page);
        _users = await UserAdminService.ListUsersAsync(TenantId);
    }

    private Task GoToPageAsync(int page) => RunAsync(() => ReloadAsync(page));

    private async Task SavePermissionsAsync()
    {
        await RunAsync(async () =>
        {
            await RoleAdminService.SetPermissionsAsync(TenantId, RoleName, ParseLines(_permissionsText));
            await ReloadAsync(_members?.Page ?? 1);
            _message = "Permissões salvas.";
        });
    }

    private async Task AddMemberAsync()
    {
        if (!Guid.TryParse(_userToAdd, out var userId))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await UserAdminService.AddRoleAsync(TenantId, userId, RoleName);
            _userToAdd = null;
            await ReloadAsync(_members?.Page ?? 1);
            _message = "Membro adicionado.";
        });
    }

    private void AskRemoveMember(RoleMemberSummary member) =>
        _pending = new PendingAction($"Remover {member.Email} do perfil {RoleName}?", async () =>
        {
            await UserAdminService.RemoveRoleAsync(TenantId, member.UserId, RoleName);
            await ReloadAsync(_members?.Page ?? 1);
            _message = "Membro removido.";
        });

    private void AskDeleteRole() =>
        _pending = new PendingAction($"Excluir o perfil {RoleName}? Esta ação não pode ser desfeita.", async () =>
        {
            await RoleAdminService.DeleteRoleAsync(TenantId, RoleName);
            Navigation.NavigateTo($"tenants/{TenantId}");
        });

    private async Task ConfirmAsync()
    {
        var pending = _pending;
        _pending = null;

        if (pending is not null)
        {
            await RunAsync(pending.Action);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        _actionError = _message = null;
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            _actionError = ApiErrorFormatter.Describe(exception.StatusCode, exception.Response);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Falha na gestão do perfil {Role} do tenant {TenantId}.", RoleName, TenantId);
            _actionError = "Falha inesperada. Tente novamente.";
        }
        finally
        {
            _busy = false;
        }
    }

    private static IReadOnlyList<string> ParseLines(string value) =>
        [.. value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
```

- [ ] **Step 3: Criar a página do usuário**

`src/AdminPortal/Secco.AdminPortal/Components/Pages/UserManagement.razor`:

```razor
@page "/tenants/{TenantId:guid}/users/{UserId:guid}"
@attribute [Authorize(Policy = AdminPortalDefaults.OperatorPolicy)]
@inject IUserAdminService UserAdminService
@inject IRoleAdminService RoleAdminService
@inject ILogger<UserManagement> Logger
@using Secco.SecureGate.Client

<PageTitle>@(_user?.Email ?? "Usuário") — Secco AdminPortal</PageTitle>

<p><a href="@($"tenants/{TenantId}")">&larr; Tenant</a></p>

@if (_loadError is not null)
{
    <p class="error" role="alert">@_loadError</p>
}
else if (_user is null)
{
    <p>Carregando…</p>
}
else
{
    <h1>@_user.Email</h1>
    <p class="muted">
        @UserStatusText.Describe(_user.Status)
        @if (_user.LockoutEnd is { } until)
        {
            <text> até @until.ToLocalTime().ToString("g")</text>
        }
        @if (_user.ExternalLogins.Count > 0)
        {
            <text> · login externo: @string.Join(", ", _user.ExternalLogins)</text>
        }
    </p>

    @if (_message is not null)
    {
        <p class="success" role="status">@_message</p>
    }
    @if (_actionError is not null)
    {
        <p class="error" role="alert">@_actionError</p>
    }
    @if (_pending is not null)
    {
        <div class="form-card" role="alertdialog">
            <p>@_pending.Message</p>
            <button @onclick="ConfirmAsync" disabled="@_busy">Confirmar</button>
            <button @onclick="() => _pending = null" disabled="@_busy">Cancelar</button>
        </div>
    }

    <section>
        <h2>Perfis</h2>
        @if (_user.Roles.Count == 0)
        {
            <p>Sem perfis — a conta não tem acesso a nada.</p>
        }
        else
        {
            <table class="grid">
                <tbody>
                    @foreach (var role in _user.Roles)
                    {
                        <tr>
                            <td><a href="@($"tenants/{TenantId}/roles/{Uri.EscapeDataString(role)}")">@role</a></td>
                            <td><button @onclick="() => AskRemoveRole(role)" disabled="@_busy">Remover</button></td>
                        </tr>
                    }
                </tbody>
            </table>
        }

        @if (AvailableRoles.Count > 0)
        {
            <div class="form-card">
                <label>Adicionar perfil
                    <select @bind="_roleToAdd">
                        <option value="">Selecione…</option>
                        @foreach (var role in AvailableRoles)
                        {
                            <option value="@role">@role</option>
                        }
                    </select>
                </label>
                <button @onclick="AddRoleAsync" disabled="@(_busy || string.IsNullOrEmpty(_roleToAdd))">Adicionar</button>
            </div>
        }
    </section>

    <section>
        <h2>Acesso efetivo</h2>
        @if (_user.EffectivePermissions.Count == 0)
        {
            <p>Nenhuma permissão.</p>
        }
        else
        {
            <ul>
                @foreach (var permission in _user.EffectivePermissions)
                {
                    <li><code>@permission</code></li>
                }
            </ul>
        }
    </section>

    <section>
        <h2>Conta</h2>
        @if (_user.Status == "Active")
        {
            <button @onclick="AskDeactivate" disabled="@_busy">Desativar</button>
            <p class="muted">Impede novo login e encerra a sessão na próxima renovação de token.</p>
        }
        else
        {
            <button @onclick="ActivateAsync" disabled="@_busy">Ativar</button>
        }
    </section>
}

@code {
    /// <summary>Tenant alvo (rota).</summary>
    [Parameter] public Guid TenantId { get; set; }

    /// <summary>Usuário alvo (rota).</summary>
    [Parameter] public Guid UserId { get; set; }

    private UserDetail? _user;
    private IReadOnlyList<RoleSummary> _roles = [];
    private string? _roleToAdd;

    private string? _loadError;
    private string? _actionError;
    private string? _message;
    private bool _busy;
    private PendingAction? _pending;

    private sealed record PendingAction(string Message, Func<Task> Action);

    private IReadOnlyList<string> AvailableRoles =>
        _user is null
            ? []
            : [.. _roles.Select(role => role.Name).Where(name => !_user.Roles.Contains(name, StringComparer.OrdinalIgnoreCase))];

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            await ReloadAsync();
        }
        catch (ApiException exception) when (exception.StatusCode == 404)
        {
            _loadError = "Usuário não encontrado.";
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Falha ao carregar o usuário {UserId} do tenant {TenantId}.", UserId, TenantId);
            _loadError = "Não foi possível carregar o usuário.";
        }
    }

    private async Task ReloadAsync()
    {
        _user = await UserAdminService.GetUserAsync(TenantId, UserId);
        _roles = await RoleAdminService.ListRolesAsync(TenantId);
    }

    private async Task AddRoleAsync()
    {
        if (string.IsNullOrEmpty(_roleToAdd))
        {
            return;
        }

        var role = _roleToAdd;

        await RunAsync(async () =>
        {
            await UserAdminService.AddRoleAsync(TenantId, UserId, role);
            _roleToAdd = null;
            await ReloadAsync();
            _message = $"Perfil {role} adicionado. Vale na próxima renovação de token do usuário.";
        });
    }

    private void AskRemoveRole(string role) =>
        _pending = new PendingAction($"Remover o perfil {role} deste usuário?", async () =>
        {
            await UserAdminService.RemoveRoleAsync(TenantId, UserId, role);
            await ReloadAsync();
            _message = $"Perfil {role} removido. Vale na próxima renovação de token do usuário.";
        });

    private void AskDeactivate() =>
        _pending = new PendingAction("Desativar esta conta? A sessão aberta termina na próxima renovação de token.", async () =>
        {
            await UserAdminService.SetActiveAsync(TenantId, UserId, active: false);
            await ReloadAsync();
            _message = "Conta desativada.";
        });

    private Task ActivateAsync() => RunAsync(async () =>
    {
        await UserAdminService.SetActiveAsync(TenantId, UserId, active: true);
        await ReloadAsync();
        _message = "Conta ativada.";
    });

    private async Task ConfirmAsync()
    {
        var pending = _pending;
        _pending = null;

        if (pending is not null)
        {
            await RunAsync(pending.Action);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        _actionError = _message = null;
        try
        {
            await action();
        }
        catch (ApiException exception)
        {
            _actionError = ApiErrorFormatter.Describe(exception.StatusCode, exception.Response);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Falha na gestão do usuário {UserId} do tenant {TenantId}.", UserId, TenantId);
            _actionError = "Falha inesperada. Tente novamente.";
        }
        finally
        {
            _busy = false;
        }
    }
}
```

- [ ] **Step 4: Compilar e testar o AdminPortal**

Run: `dotnet build src/AdminPortal/Secco.AdminPortal/Secco.AdminPortal.csproj -c Release`
Expected: 0 avisos, 0 erros.

Run: `dotnet test tests/AdminPortal/Secco.AdminPortal.Tests/Secco.AdminPortal.Tests.csproj -c Release`
Expected: PASS.

- [ ] **Step 5: Verificação manual na aplicação**

Subir o ambiente de DEV (configuração do `.vscode/launch.json` "federado RS256 via SecureGate", com o `docker-compose.yml` da raiz) e, logado como o operador demo no AdminPortal (`https://localhost:5001`):
1. Abrir um tenant → a lista de usuários mostra a coluna Situação.
2. Criar um perfil `teste-perfil`, abrir a página dele, adicionar um membro, remover o membro (com confirmação), excluir o perfil.
3. Abrir a página de um usuário → adicionar/remover perfil, ver o acesso efetivo mudar, desativar e ativar.
4. No tenant de plataforma, abrir o próprio usuário operador e tentar remover `installation-operator` → mensagem de 409 exibida.

- [ ] **Step 6: Commit**

```bash
git add src/AdminPortal
git commit -m "feat(adminportal): páginas de perfil e de usuário (#26)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 11: Mutação, suíte completa e documentação

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/roadmap.md`
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-09-16-gestao-de-perfis-design.md`

- [ ] **Step 1: Verificação por mutação**

Para cada linha da tabela: aplicar a mutação, rodar o filtro, confirmar que **pelo menos um** dos testes esperados falha, restaurar o arquivo (`git checkout -- <arquivo>`) e conferir `git diff --stat` vazio para ele. Uma mutação que sobrevive é um teste faltando — escrevê-lo antes de seguir.

Filtro: `dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release --filter "FullyQualifiedName~RoleProfileManagementTests|FullyQualifiedName~UserProfileManagementTests|FullyQualifiedName~ProfileAssignmentTokenTests|FullyQualifiedName~LastOperatorGuardTests|FullyQualifiedName~OperatorDemotionTests"`

| # | Arquivo | Trocar | Por | Deve derrubar |
| --- | --- | --- | --- | --- |
| M1 | `Api/Endpoints/TokenEndpoints.cs` | `InstallationOperatorPolicy.FilterScopes(user, roles, stored!.GetScopes())` | `[.. stored!.GetScopes()]` | `Refresh_AposRetirarDoPerfilDeOperador_PerdeScopeAdminERecebeTenant` |
| M2 | `Application/Users/AddUserRoleHandler.cs` | `if (!RoleInputRules.IsAssignableToUsers(name))` | `if (!RoleInputRules.IsAssignableToUsers(name) && string.IsNullOrEmpty("mutante"))` | `AddUserRole_PerfilSoDeToken_Retorna400` |
| M3 | `Application/Users/CreateUserHandler.cs` | `if (!RoleInputRules.IsAssignableToUsers(role))` | `if (!RoleInputRules.IsAssignableToUsers(role) && string.IsNullOrEmpty("mutante"))` | `CreateUser_ComPerfilSoDeToken_Retorna400` |
| M4 | `Application/Roles/DeleteRoleHandler.cs` | `if (RoleInputRules.IsReservedName(name))` | `if (RoleInputRules.IsReservedName(name) && string.IsNullOrEmpty("mutante"))` | `DeleteRole_OperadorDaInstalacao_Retorna409` |
| M5 | `Infrastructure/Roles/RoleRepository.cs` | `if (await context.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, cancellationToken).ConfigureAwait(false))` | `if (await context.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, cancellationToken).ConfigureAwait(false) && string.IsNullOrEmpty("mutante"))` | `DeleteRole_ComMembros_Retorna409EMantem` |
| M6 | `Application/Users/RemoveUserRoleHandler.cs` | `if (string.Equals(command.CallerSubject, command.UserId.ToString(), StringComparison.OrdinalIgnoreCase))` | `if (string.Equals(command.CallerSubject, command.UserId.ToString(), StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty("mutante"))` | `Remover_ASiMesmoDoPerfilDeOperador_Retorna409` |
| M7 | `Application/Users/RemoveUserRoleHandler.cs` | `if (await OperatorGuard.WouldLeaveNoActiveOperatorAsync(` | `if (string.IsNullOrEmpty("mutante") && await OperatorGuard.WouldLeaveNoActiveOperatorAsync(` | `Remover_UltimoOperadorAtivo_Retorna409` |
| M8 | `Application/Users/SetUserActivationHandler.cs` | `&& await OperatorGuard.WouldLeaveNoActiveOperatorAsync(` | `&& string.IsNullOrEmpty("mutante") && await OperatorGuard.WouldLeaveNoActiveOperatorAsync(` | `Desativar_UltimoOperadorAtivo_Retorna409` |
| M9 | `Infrastructure/Users/UserAccountService.cs` | `&& (!user.LockoutEnabled \|\| user.LockoutEnd == null \|\| user.LockoutEnd <= now)` | (remover a linha) | `Remover_QuandoOOutroEstaDesativado_Retorna409` |
| M10 | `Infrastructure/Users/UserAccountService.cs` | `.Where(role => role.TenantId == tenantId && role.NormalizedName == normalized)` (em `FindRoleIdAsync`) | `.Where(role => role.NormalizedName == normalized)` | `AddUserRole_PerfilSoDeOutroTenant_Retorna404` |
| M11 | `Infrastructure/Roles/RoleRepository.cs` | `.Where(r => r.TenantId == tenantId && r.NormalizedName == normalized)` (em `FindRoleAsync`) | `.Where(r => r.NormalizedName == normalized)` | `GetRole_DeOutroTenant_Retorna404` |
| M12 | `Infrastructure/Users/UserAccountService.cs` | `.Select(login => login.LoginProvider)` | `.Select(login => login.LoginProvider + ":" + login.ProviderKey)` | `GetUser_ComLoginEntra_ListaSoOProvedor` |
| M13 | `Application/Users/GetUserHandler.cs` | `effective.UnionWith(granted.Value);` | `effective.UnionWith(granted.Value.Take(1));` | `GetUser_Ativo_DevolvePerfisEPermissoesEfetivasSemRepeticao` |

(M10: `FindRoleIdAsync` com mais de um homônimo devolve o primeiro; o teste cria o homônimo só no outro tenant, então a mutação o encontra e atribui.)

Registrar o placar (ex.: "13 de 13 detectadas") para o roadmap.

- [ ] **Step 2: Suíte completa**

Run: `dotnet build Secco.Platform.slnx --configuration Release`
Expected: 0 erros; nenhum aviso novo em projeto de produto (os avisos de teste pré-existentes são CA1001/CA1822/CA1848/CA1861 em arquivos antigos — conferir que nenhum aponta para arquivo criado neste plano; se apontar, corrigir).

Run: `dotnet test Secco.Platform.slnx -c Release --no-build`
Expected: todos PASS. Registrar o total e o total do SecureGate (antes: 833 na solution, 297 no SecureGate).

- [ ] **Step 3: Documentação**

`CHANGELOG.md`, seção `## Não publicado` — substituir `_Nada pendente._ ...` por:

```markdown
### Secco.SecureGate.Client

- **Adicionado (aditivo)** gestão de perfis sobre o Identity (issue #26): `GetRoleAsync`, `DeleteRoleAsync`, `ListRoleMembersAsync` (paginado), `GetUserAsync` (situação, perfis, permissões efetivas e provedores de login externo — nunca o identificador do diretório), `AddUserRoleAsync` e `RemoveUserRoleAsync`, ambos idempotentes.
- **Adicionado (aditivo)** `UserDto.Status` (`Active`, `Deactivated`, `LockedOut`).
- **Novas recusas em operações existentes:** `CreateUserAsync` responde **400** (`SecureGate.User.RoleNotAssignable`) para perfis reservados que são identidades só de token; `DeactivateUserAsync` responde **409** quando deixaria a instalação sem operador ativo.
- **Mudança de comportamento do servidor:** a renovação de token passou a reaplicar o filtro de operador aos scopes. Um operador retirado do perfil `installation-operator` perde `securegate:admin` e volta a ter `tenant_id` na renovação seguinte — antes, seguia renovando `securegate:admin` indefinidamente.
- Nenhum método existente foi renomeado; o contrato não perdeu nenhuma operação.
```

`docs/roadmap.md` — inserir antes de `## Backlog`:

```markdown
- [x] **Gestão de perfis de acesso** (issue #26, 2026-09-16, spec `docs/superpowers/specs/2026-09-16-gestao-de-perfis-design.md`): o SecureGate só atribuía perfil na criação do usuário. **Perfil = Role nativo do Identity** (`tb_roles` + `tb_role_claims` + `tb_user_roles`, ADR-0021 intacta) — sem tabela, migração nem ADR; descartado o aninhamento usuário → perfil → roles sugerido na issue. Entregue: ver e excluir perfil (vazio, nunca reservado), membros paginados, atribuir e remover perfil de usuário existente (idempotentes), detalhe do usuário com permissões efetivas calculadas pela mesma resolução dos produtos, situação da conta na listagem, e as páginas de perfil e de usuário no AdminPortal. **Guardas:** perfis só de token nunca atribuíveis (nos dois caminhos), ninguém se remove do perfil de operador, e nem remover nem desativar pode deixar a instalação sem operador ativo — inclusive por client de máquina. **Defeito próprio corrigido:** a renovação copiava os scopes do token anterior, e um operador rebaixado seguiria renovando `securegate:admin` para sempre. Guarda de código contra as APIs de role do Identity que ignoram o tenant. Mutação: <PLACAR DO STEP 1>. Verificado: <TOTAIS DO STEP 2>.
```

(substituir os dois marcadores `<...>` pelos números reais dos Steps 1 e 2 antes de salvar.)

`CLAUDE.md`, no parágrafo "Estado atual", logo antes de `Aberta a issue #6 — **"acessar como"**`:

```markdown
**Gestão de perfis** (issue #26, 2026-09-16): perfil = Role nativo do Identity (ADR-0021 intacta, sem tabela nova); ver/excluir perfil, membros, atribuir/remover perfil de usuário existente e detalhe com acesso efetivo, na API e no AdminPortal; perfis só de token não atribuíveis, e a instalação nunca fica sem operador ativo; a renovação de token passou a reaplicar o filtro de operador aos scopes.
```

Spec `docs/superpowers/specs/2026-09-16-gestao-de-perfis-design.md` — alinhar com o implementado:
1. Tabela de perfis, linha `ListRoleMembers`: trocar `` `size` ≤ 100 `` por `tamanho limitado pelo SharedKernel (200)`.
2. Tabela de usuários, linha `AddUserRole`: trocar `409 não atribuível` por `400 não atribuível`.

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md docs/roadmap.md CLAUDE.md docs/superpowers/specs/2026-09-16-gestao-de-perfis-design.md
git commit -m "docs: gestão de perfis de acesso (#26)

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Parar e perguntar**

Não empurrar. Perguntar ao usuário se pode:
1. `git push origin main`;
2. publicar `securegate-client/v0.7.0` seguindo a skill `secco-platform-release` — rodar `python scripts/check-release-chain.py securegate-client/v` e taguear no mesmo commit as dependências que ela apontar, empurrando **uma tag por vez** e esperando cada publicação;
3. atualizar CHANGELOG/README/getting-started contra o feed depois da publicação;
4. fechar a issue #26 com comentário: perfil = Role nativo do Identity; a camada de grupo acima do role sugerida no comentário da issue ficou de fora por exigir tabela, migração, expansão no token e ADR sem ganho demonstrado; listar as rotas entregues.
