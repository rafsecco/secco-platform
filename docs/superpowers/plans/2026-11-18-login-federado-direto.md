# Login federado direto — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Com a opção ligada na federação do tenant, o `/connect/authorize` sem sessão vai direto ao Entra (diretório fixado, `domain_hint`/`login_hint`), sem laço em nenhum caminho, com a página "Você saiu" depois do logout e `/login` como saída de emergência.

**Architecture:** A decisão mora no authorize (`DirectSignInPolicy`, a partir do registro do client, ADR-0040). O desafio ao Entra carrega os dados por requisição em `AuthenticationProperties.Items`, e um evento do esquema OIDC existente os aplica. O callback e o `EntraSignInProcessor` não mudam. Há duas marcas contra laço e login automático: o parâmetro `secco_direct_attempt=1` no retorno ao authorize e o cookie de logout.

**Tech Stack:** .NET / ASP.NET Core minimal APIs + Razor Pages, OpenIddict, `Microsoft.AspNetCore.Authentication.OpenIdConnect`, EF Core (SQL Server + PostgreSQL), xUnit + FluentAssertions 7 + Testcontainers, Blazor Server (AdminPortal).

**Spec:** [docs/superpowers/specs/2026-11-18-login-federado-direto-design.md](../specs/2026-11-18-login-federado-direto-design.md)

## Global Constraints


- Opção desligada (padrão) = comportamento idêntico ao de hoje em todo caminho.
- O modo direto só vale **sem cookie de sessão nenhum**. Cookie inválido ou conta recusada continuam indo para a tela local.
- A decisão de ir direto vem do registro do client (`OidcApplication.TenantId`) e da federação no banco, nunca de parâmetro. Parâmetro da requisição só pode **desligar** o modo direto.
- `/login` nunca redireciona sozinha.
- `login_hint`: até 254 caracteres, sem caractere de controle nem espaço em branco, exatamente um `@` com as duas partes não vazias. Fora disso, é descartado em silêncio.
- Marca de logout: cookie `HttpOnly`, `SameSite=Lax`, `Secure` em Production ou em requisição HTTPS, sem `Expires` (cookie de sessão do navegador), valor = instante em segundos Unix, ignorada quando tem mais de 12 h.
- `returnUrl` sempre só local (começa com `/`, não com `//` nem `/\`).
- Mensagem de erro federado: a genérica de hoje (`LoginModel.FederatedErrorMessage`), sem revelar o motivo (ADR-0020).
- DTO e request posicionais: campo novo **no fim** (consumidos pelo NSwag).
- Migration em `tb_tenant_federations` (sob RLS): `TenantRowLevelSecurity.Drop` no começo e `Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054)` no fim, nos dois engines.
- Build com warnings = erros: `dotnet build Secco.Platform.slnx --configuration Release`.
- FluentAssertions fixado na v7; versões só em `Directory.Packages.props`; nenhuma dependência nova.
- Commits Conventional Commits, direto na `main`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Navegador que não guarda o cookie de sessão na volta do Entra** (proxy sem HTTPS, SameSite). O esperado é parar na tela local com a mensagem genérica, sem laço. Fixado na Tarefa 5 (`Authorize_SemSessaoComMarcaDeTentativa_VaiParaLoginComErro`).
2. **Duas abas abrindo produtos ao mesmo tempo, sem sessão.** O esperado é as duas irem ao Entra, sem erro falso. Fixado na Tarefa 5 (`Authorize_DuasRequisicoesSemSessao_AmbasVaoAoEntra`).
3. **`login_hint` hostil vindo da aplicação** (CRLF, 2 KB, sem `@`). O esperado é ser descartado, sem chegar à URL do Entra. Fixado nas Tarefas 3 e 5.
4. **Federação desabilitada com `DirectSignIn` ainda ligado.** O esperado é a tela local. Fixado na Tarefa 3 (`Resolve_FederacaoDesabilitada_Nulo`).
5. **Authority do Entra já específica de um diretório, ou de nuvem soberana.** O esperado é não reescrever o host e não quebrar a URL. Fixado na Tarefa 4 (`PinDirectory_*`).

---

### Tarefa 1: `DirectSignIn` na federação — domínio, API, migration e client

**Files:**

- Modify: `src/SecureGate/Secco.SecureGate.Domain/Tenants/TenantFederation.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Tenants/UpsertTenantFederationHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Tenants/TenantDtos.cs` (`TenantFederationDto`)
- Modify: `src/SecureGate/Secco.SecureGate.Api/Requests/TenantRequests.cs` (`UpsertTenantFederationRequest`)
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/TenantEndpoints.cs:144-158` (repassar o campo e atualizar o `WithSummary`)
- Create: migrations `AddFederationDirectSignIn` em `src/SecureGate/Secco.SecureGate.Migrations.SqlServer/Migrations/` e `src/SecureGate/Secco.SecureGate.Migrations.Postgres/Migrations/`
- Modify: `src/SecureGate/Secco.SecureGate.Api/openapi/openapi.json` (regerado) e o client gerado de `Secco.SecureGate.Client`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/TenantFederationTests.cs`, `tests/SecureGate/Secco.SecureGate.Tests/Unit/UpsertTenantFederationHandlerTests.cs`, `tests/SecureGate/Secco.SecureGate.Tests/Integration/TenantFederationManagementTests.cs`

**Interfaces:**

- Produces: `TenantFederation.DirectSignIn { get; private set; }` (`bool`, padrão `false`, coluna `fl_direct_sign_in` pela convention, sem mapeamento à mão); `void TenantFederation.SetDirectSignIn(bool enabled)` (atualiza `UpdatedAt`).
- Produces: `UpsertTenantFederationCommand(..., IReadOnlyList<string>? AllowedEmailDomains = null, bool? DirectSignIn = null)` — `null` mantém; na criação, `null` = `false`.
- Produces: `UpsertTenantFederationRequest(Guid? DirectoryId, bool? Enabled, IReadOnlyList<string>? AllowedEmailDomains = null, bool? DirectSignIn = null)`.
- Produces: `TenantFederationDto(..., IReadOnlyList<string>? AllowedEmailDomains = null, bool DirectSignIn = false)`; no client NSwag: `TenantFederationDto.DirectSignIn` e `UpsertTenantFederationRequest.DirectSignIn`.

- [ ] **Passo 1: Testes que falham**
  - `TenantFederationTests.NovaFederacao_DirectSignInDesligado` → `new TenantFederation(t, d).DirectSignIn.Should().BeFalse()`.
  - `TenantFederationTests.SetDirectSignIn_LigaEAtualizaUpdatedAt` → `DirectSignIn` `true` e `UpdatedAt` maior ou igual ao anterior.
  - `UpsertTenantFederationHandlerTests.Upsert_DirectSignInNulo_MantemOValorAtual` → federação existente com `true`, comando com `null`, continua `true`.
  - `UpsertTenantFederationHandlerTests.Upsert_DirectSignInInformado_Grava` → criação com `true` grava `true`.
  - `TenantFederationManagementTests.UpsertFederation_ComDirectSignIn_GetTenantDevolveOCampo` → `PUT` com `directSignIn = true`, depois `GET /api/v1/tenants/{id}` devolve `federation.directSignIn == true`.
  - `TenantFederationManagementTests.UpsertFederation_SemDirectSignIn_MantemOValor` → `PUT` com `true`, `PUT` sem o campo, `GET` continua `true`.

- [ ] **Passo 2: Rodar e ver falhar**
  Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~TenantFederation|FullyQualifiedName~UpsertTenantFederationHandler"`
  Expected: falha de compilação (`DirectSignIn` não existe).


- [ ] **Passo 3: Implementar** a propriedade, o setter, o campo no comando, o handler (`if (command.DirectSignIn is { } direct) federation.SetDirectSignIn(direct)` nos dois ramos), o request, o DTO (`FromEntity` projeta) e o endpoint.

- [ ] **Passo 4: Gerar as migrations**
  Run: `dotnet ef migrations add AddFederationDirectSignIn --project src/SecureGate/Secco.SecureGate.Migrations.SqlServer --output-dir Migrations` e o mesmo com `Secco.SecureGate.Migrations.Postgres`.
  Depois, editar o `Up` e o `Down` de cada uma: `TenantRowLevelSecurity.Drop(migrationBuilder)` como primeira linha e `TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054)` como última. Acrescentar o `<summary>` no padrão de `20261118010705_AddDirectoryAttributes.cs`. Conferir que a coluna saiu `fl_direct_sign_in`, `bit`/`boolean`, `NOT NULL DEFAULT false`.


- [ ] **Passo 5: Regenerar o contrato e o client**
  Run: `SECCO_UPDATE_OPENAPI=true dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~OpenApiContractTests"` e depois `dotnet build src/SecureGate/Secco.SecureGate.Client`.
  Expected: `openapi.json` com `directSignIn` no fim de `TenantFederationDto` e de `UpsertTenantFederationRequest`; build verde.


- [ ] **Passo 6: Rodar e ver passar**
  Run: o filtro do Passo 2 mais `FullyQualifiedName~OpenApiContractTests|FullyQualifiedName~RowLevelSecurity|FullyQualifiedName~PostgresParity`.
  Expected: PASS.


- [ ] **Passo 7: Commit** — `feat(securegate): opcao de login federado direto na federacao do tenant`

---

### Tarefa 2: AdminPortal — caixa "Ir direto ao Entra"

**Files:**

- Modify: `src/AdminPortal/Secco.AdminPortal/Services/AdminModels.cs` (`TenantFederationSummary`)
- Modify: `src/AdminPortal/Secco.AdminPortal/Services/ITenantAdminService.cs` e `SecureGateTenantAdminService.cs` (`SetFederationAsync`, `GetTenantAsync`)
- Modify: `src/AdminPortal/Secco.AdminPortal/Components/Pages/TenantManagement.razor:329-360, 445-447, 684-700`
- Test: `tests/AdminPortal/Secco.AdminPortal.Tests/DirectoryProvisioningAdminTests.cs` (mesmo padrão de dublê de client dos testes `SetFederation_*`)

**Interfaces:**

- Consumes: `UpsertTenantFederationRequest.DirectSignIn` e `TenantFederationDto.DirectSignIn` do client (Tarefa 1).
- Produces: `TenantFederationSummary(Guid DirectoryId, bool IsEnabled, DateTimeOffset? UpdatedAt, IReadOnlyList<string>? AllowedEmailDomains = null, bool DirectSignIn = false)`; `SetFederationAsync(Guid tenantId, Guid directoryId, bool enabled, IReadOnlyList<string>? allowedEmailDomains = null, bool? directSignIn = null, CancellationToken cancellationToken = default)`.

- [ ] **Passo 1: Testes que falham**
  - `SetFederation_ComDirectSignIn_EnviaOCampo` → o request capturado tem `DirectSignIn == true`.
  - `SetFederation_SemDirectSignIn_NaoMandaOCampo` → `DirectSignIn == null`.
  - `GetTenant_ProjetaODirectSignIn` → `DirectSignIn == true` no resumo.
- [ ] **Passo 2: Rodar e ver falhar** — `dotnet test tests/AdminPortal/Secco.AdminPortal.Tests --filter "FullyQualifiedName~DirectoryProvisioningAdminTests"` (falha de compilação).
- [ ] **Passo 3: Implementar.** Na tela: leitura `<dt>Login direto</dt><dd>Ligado — vai direto ao Entra, sem a tela do SecureGate</dd>` ou `Desligado`. No formulário: `<label><input type="checkbox" @bind="_federationDirectSignIn" /> Ir direto ao Entra (sem a tela de login do SecureGate)</label>`, com o campo pré-carregado do resumo como os outros. O `SaveFederationAsync` passa `_federationDirectSignIn`.
- [ ] **Passo 4: Rodar e ver passar.**
- [ ] **Passo 5: Commit** — `feat(adminportal): liga e desliga o login federado direto do tenant`

---

### Tarefa 3: Decisão "ir direto?", `login_hint` e marcas

**Files:**

- Create: `src/SecureGate/Secco.SecureGate.Api/Identity/DirectSignInPolicy.cs`
- Create: `src/SecureGate/Secco.SecureGate.Api/Identity/LoginHint.cs`
- Create: `src/SecureGate/Secco.SecureGate.Api/Identity/SignInMarkers.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Extensions/SecureGateIdentityExtensions.cs:34` (registro `AddScoped<DirectSignInPolicy>()`) e `:80` (o `OnSigningIn` também limpa a marca de logout)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/LoginHintTests.cs`, `tests/SecureGate/Secco.SecureGate.Tests/Unit/SignInMarkersTests.cs`, `tests/SecureGate/Secco.SecureGate.Tests/Integration/DirectSignInPolicyTests.cs` (collection `SharedApiCollectionDefinition`)

**Interfaces:**

- Produces: `internal sealed record DirectSignInTarget(Guid TenantId, Guid DirectoryId, string? DomainHint)`.
- Produces: `internal sealed class DirectSignInPolicy(IOpenIddictApplicationManager applications, ITenantRepository tenants, ITenantAccessScope accessScope, IOptions<SecureGateEntraIdOptions> entra)` com `Task<DirectSignInTarget?> ResolveAsync(string? clientId, CancellationToken cancellationToken)`. Devolve um alvo só se: o Entra está configurado, o client existe e é `OidcApplication { TenantId: { } }`, e a federação desse tenant (lida dentro de `accessScope.EnterTenant(tenantId)`) existe com `IsEnabled && DirectSignIn`. O `DomainHint` é o primeiro de `GetAllowedEmailDomains()`, ou `null`.
- Produces: `internal static class LoginHint { public const int MaxLength = 254; public static string? Sanitize(string? value); }`.
- Produces: `internal static class SignInMarkers` com `const string SignedOutCookie = "secco.securegate.signed-out"`, `static readonly TimeSpan SignedOutMaxAge = TimeSpan.FromHours(12)`, `const string DirectAttemptParameter = "secco_direct_attempt"`, `void MarkSignedOut(HttpContext context, DateTimeOffset now)`, `bool HasSignedOut(HttpContext context, DateTimeOffset now)`, `void ClearSignedOut(HttpContext context)`, `string WithDirectAttempt(string localUrl)` (acrescenta `secco_direct_attempt=1` com `QueryHelpers.AddQueryString`) e `bool HasDirectAttempt(HttpRequest request)`. O `Secure` do cookie é `IHostEnvironment.IsProduction() || context.Request.IsHttps` (ambiente lido de `context.RequestServices`).

- [ ] **Passo 1: Testes que falham**
  - `LoginHintTests` (Theory): `"ana@abbc.com.br"` → igual; `null`/`""` → `null`; 255 caracteres com `@` → `null`; `"ana@abbc.com\r\nX: y"` → `null`; `"ana abbc@x"` → `null`; `"sem-arroba"` → `null`; `"a@b@c"` → `null`; `"@abbc.com"` → `null`; `"ana@"` → `null`.
  - `SignInMarkersTests` (com `DefaultHttpContext` e `ServiceCollection` contendo um `IHostEnvironment` dublê):
    - `MarkSignedOut_GravaCookieHttpOnlyLaxSemExpires` (cabeçalho `Set-Cookie` contém `httponly`, `samesite=lax` e não contém `expires`);
    - `HasSignedOut_DentroDe12h_Verdadeiro` / `HasSignedOut_Com12hE1s_Falso` / `HasSignedOut_ValorNaoNumerico_Falso` / `HasSignedOut_SemCookie_Falso`;
    - `WithDirectAttempt_PreservaAQueryOriginal` (`"/connect/authorize?client_id=x&state=y"` → contém `client_id=x`, `state=y` e `secco_direct_attempt=1`);
    - `HasDirectAttempt_SoComValor1`.
  - `DirectSignInPolicyTests` (integração, collection `EntraFederatedApiCollectionDefinition` criada nesta tarefa — ver "Produces (teste)" abaixo; policy resolvida de `factory.Services.CreatePlatformScope()`):
    - `Resolve_ClientDeTenantComDirectSignIn_DevolveDiretorioEDominio` (`DomainHint == "abbc.com.br"`, o primeiro da lista);
    - `Resolve_SemDominios_DomainHintNulo`;
    - `Resolve_DirectSignInDesligado_Nulo`;
    - `Resolve_FederacaoDesabilitada_Nulo`;
    - `Resolve_TenantSemFederacao_Nulo`;
    - `Resolve_ClientSemTenant_Nulo`;
    - `Resolve_ClientInexistente_Nulo`.
  - `DirectSignInPolicyUnitTests.Resolve_EntraNaoConfigurado_NuloSemConsultarNada` (unit, em `tests/SecureGate/Secco.SecureGate.Tests/Unit/`): `IOptions` sem `ClientId`, dependências como substitutos NSubstitute, `Received(0)` no `FindByClientIdAsync`.

**Produces (teste)** — nasce aqui, a Tarefa 5 reusa:

- `tests/SecureGate/Secco.SecureGate.Tests/Integration/EntraFederatedSecureGateApiFactory.cs`: `EntraFederatedSecureGateApiFactory : SecureGateApiFactory`, que define `SecureGate:EntraId:ClientId`/`ClientSecret` e, em `ConfigureTestServices`, faz `services.Configure<OpenIdConnectOptions>(SecureGateEntraIdOptions.AuthenticationScheme, o => o.Configuration = new OpenIdConnectConfiguration { AuthorizationEndpoint = "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize", TokenEndpoint = "https://login.microsoftonline.com/organizations/oauth2/v2.0/token" })`. Usa `Configure`, não `PostConfigure`, para o post-configure do handler criar o `StaticConfigurationManager` e nada sair para a rede.
- `EntraFederatedApiCollectionDefinition` em `TestCollections.cs`.
- `SecureGateApiFactory.CreateTenantPublicClientAsync(Guid tenantId, string clientId, string redirectUri, params string[] scopes)`: um client público como o `CreatePublicClientAsync`, mas criado como `OidcApplication { TenantId = tenantId }`.
- [ ] **Passo 2: Rodar e ver falhar** — `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~LoginHintTests|FullyQualifiedName~SignInMarkersTests|FullyQualifiedName~DirectSignInPolicyTests"`.
- [ ] **Passo 3: Implementar** as três classes, o registro, a factory de teste, a collection e o helper. No `OnSigningIn`: `options.Events.OnSigningIn = async context => { SignInMarkers.ClearSignedOut(context.HttpContext); await FreshAuthentication.OnSigningInAsync(context); };`.
- [ ] **Passo 4: Rodar e ver passar.**
- [ ] **Passo 5: Commit** — `feat(securegate): decisao do login federado direto e marcas contra laco`

---

### Tarefa 4: Desafio ao Entra com diretório fixado e retorno de erro com `returnUrl`

**Files:**

- Create: `src/SecureGate/Secco.SecureGate.Api/Identity/EntraDirectChallenge.cs`
- Create: `src/SecureGate/Secco.SecureGate.Api/Identity/FederatedFailureRedirect.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Extensions/SecureGateEntraFederationExtensions.cs:90-100` (`OnRedirectToIdentityProvider` novo; `OnRemoteFailure` passa a usar `FederatedFailureRedirect.FromCallbackRedirectUri(context.Properties?.RedirectUri)`)
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/FederatedLoginEndpoints.cs` (`GenericFailureRedirect` e `ToLocalUrl` dão lugar a `FederatedFailureRedirect.For(returnUrl)` e `FederatedFailureRedirect.IsLocal`)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/EntraDirectChallengeTests.cs`, `tests/SecureGate/Secco.SecureGate.Tests/Unit/FederatedFailureRedirectTests.cs`

**Interfaces:**

- Consumes: `DirectSignInTarget`, `LoginHint` (Tarefa 3).
- Produces: `internal static class EntraDirectChallenge`:
  - `AuthenticationProperties Create(string localReturnUrl, DirectSignInTarget target, string? loginHint, bool forceLogin)`: `RedirectUri = "/login/entra-callback?returnUrl=" + Uri.EscapeDataString(localReturnUrl)`. Os itens `secco.direct.directory` (GUID `D`), `secco.direct.domain_hint`, `secco.direct.login_hint` e `secco.direct.force_login` (`"1"`) só entram quando há valor. O `loginHint` já chega sanitizado.
  - `Task OnRedirectToIdentityProvider(RedirectContext context)`: não faz nada sem o item de diretório. Com ele, `IssuerAddress = PinDirectory(IssuerAddress, dir) ?? IssuerAddress`, `DomainHint`, `LoginHint` e `Prompt = "login"` quando forçado.
  - `string? PinDirectory(string authorizationEndpoint, Guid directoryId)`: URI absoluta `https`, com o **primeiro** segmento do path igual (sem diferenciar maiúsculas) a `organizations` ou `common`. Troca só esse segmento por `directoryId.ToString("D")` e preserva esquema, host, porta, o resto do path e a query. Em qualquer outro caso, `null`.
- Produces: `internal static class FederatedFailureRedirect` com `const string Path = "/login?federatedError=1"`, `bool IsLocal(string? url)` (a regra atual de `ToLocalUrl`), `string For(string? returnUrl)` (local → `Path + "&returnUrl=" + Uri.EscapeDataString(returnUrl)`; senão `Path`) e `string FromCallbackRedirectUri(string? redirectUri)` (extrai o `returnUrl` da query de `/login/entra-callback?...` e delega a `For`).

- [ ] **Passo 1: Testes que falham**
  - `PinDirectory_Organizations_TrocaPeloDiretorio` (`https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize` → `https://login.microsoftonline.com/{guid}/oauth2/v2.0/authorize`).
  - `PinDirectory_Common_TrocaPeloDiretorio`.
  - `PinDirectory_NuvemSoberana_PreservaOHost` (`https://login.microsoftonline.us/organizations/...` → host `login.microsoftonline.us`).
  - `PinDirectory_JaEspecifico_Nulo` (primeiro segmento é um GUID ou nome de tenant).
  - `PinDirectory_Http_Nulo`; `PinDirectory_Relativo_Nulo`.
  - `Create_SemLoginHintNemDominio_SoDiretorio` (`Items` não contém as chaves de hint).
  - `Create_RedirectUriApontaParaOCallbackComReturnUrlEscapado`.
  - `OnRedirect_ComItens_AplicaEndpointDominioLoginHintEPrompt` (monta `RedirectContext` com `OpenIdConnectMessage { IssuerAddress = ".../organizations/oauth2/v2.0/authorize" }`).
  - `OnRedirect_SemItemDeDiretorio_NaoMexeEmNada` (o botão "Entrar com Microsoft" da `/login` segue igual).
  - `FederatedFailureRedirect_For_Local_PreservaReturnUrl` / `_Absoluta_Descarta` / `_BarraDupla_Descarta` / `_Nulo_SoOPath`.
  - `FederatedFailureRedirect_FromCallbackRedirectUri_ExtraiOReturnUrl` / `_SemReturnUrl_SoOPath`.
- [ ] **Passo 2: Rodar e ver falhar** — `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~EntraDirectChallengeTests|FullyQualifiedName~FederatedFailureRedirectTests"`.
- [ ] **Passo 3: Implementar** as duas classes e os dois eventos. O callback usa `FederatedFailureRedirect.For(returnUrl)` nas duas recusas e `FederatedFailureRedirect.IsLocal` no `LocalRedirect` de sucesso.
- [ ] **Passo 4: Rodar e ver passar**, junto com `FullyQualifiedName~EntraSignInProcessorTests|FullyQualifiedName~DirectoryJitLoginTests` (sem regressão).
- [ ] **Passo 5: Commit** — `feat(securegate): desafio ao Entra com diretorio fixado e hints`

---

### Tarefa 5: Authorize vai direto ao Entra

**Files:**

- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/InteractiveEndpoints.cs:56-117`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/DirectSignInAuthorizeTests.cs`

**Interfaces:**

- Consumes: `DirectSignInPolicy.ResolveAsync`, `SignInMarkers.*`, `LoginHint.Sanitize`, `EntraFederatedSecureGateApiFactory`, `EntraFederatedApiCollectionDefinition`, `CreateTenantPublicClientAsync` (Tarefa 3); `EntraDirectChallenge.Create`, `FederatedFailureRedirect.For` (Tarefa 4).
- Atenção: o OpenIddict precisa aceitar o parâmetro extra `secco_direct_attempt` no authorize. Parâmetro desconhecido não é rejeitado por padrão, e o teste `Authorize_SemSessaoComMarcaDeTentativa_*` prova isso.

Comportamento a implementar no `AuthorizeAsync` (a ordem importa):

1. **Sem cookie** (`!authentication.Succeeded`):
   - `target = await policy.ResolveAsync(request.ClientId, ct)`. Com `null`, faz o `Challenge` do Identity de hoje.
   - Com `prompt=none`: `Forbid` com `Errors.LoginRequired`, mesma forma do bloco existente.
   - Com `SignInMarkers.HasDirectAttempt(context.Request)`: `Results.Redirect(FederatedFailureRedirect.For(urlAtual))`.
   - Com `SignInMarkers.HasSignedOut(context, DateTimeOffset.UtcNow)`: `Results.Redirect("/saiu?returnUrl=" + Uri.EscapeDataString(urlAtual))`.
   - Senão: `Results.Challenge(EntraDirectChallenge.Create(SignInMarkers.WithDirectAttempt(urlAtual), target, LoginHint.Sanitize(request.LoginHint), forceLogin: false), [SecureGateEntraIdOptions.AuthenticationScheme])`.
2. **Cookie inválido ou conta recusada** (bloco das linhas 74-90): **sem mudança**.
3. **Reautenticação** (`prompt=login` ou `max_age` vencido, depois do `SignOutAsync`): `target = ResolveAsync(...)`. Com alvo, desafia o Entra com `WithDirectAttempt(WithoutLoginPrompt(...))` e `forceLogin: true`. Sem alvo, faz o de hoje. O ramo `prompt=none` + `prompt=login` existente fica antes, igual.

`urlAtual` = `context.Request.PathBase + context.Request.Path + context.Request.QueryString`, a expressão já usada no arquivo.


- [ ] **Passo 1: Testes que falham** (`DirectSignInAuthorizeTests`, collection `EntraFederatedApiCollectionDefinition`). O setup cria um tenant com federação (`DirectSignIn = true`, domínios `["abbc.com.br"]`), uma pessoa dona com senha e um client público vinculado. As requisições usam `OidcLoginDriver.CreateBrowser()` (sem auto-redirect, com cookies) e um authorize com PKCE e `login_hint=ana@abbc.com.br`.
  - `Authorize_SemSessao_OpcaoLigada_VaiAoEntraDoDiretorio` → `302`; `Location` começa com `https://login.microsoftonline.com/{directoryId}/oauth2/v2.0/authorize` e tem `domain_hint=abbc.com.br` e `login_hint=ana%40abbc.com.br`, sem `prompt`.
  - `Authorize_SemSessao_OpcaoDesligada_VaiParaLogin` → `Location` começa com `/login` (desligar por `PUT` sem mudar o resto).
  - `Authorize_SemSessao_ClientSemTenant_VaiParaLogin` (client de `CreatePublicClientAsync`).
  - `Authorize_LoginHintHostil_NaoChegaAoEntra` (`login_hint` com `%0D%0A`) → `Location` sem `login_hint`.
  - `Authorize_SemSessaoComMarcaDeTentativa_VaiParaLoginComErro` → `Location` começa com `/login?federatedError=1&returnUrl=`.
  - `Authorize_DuasRequisicoesSemSessao_AmbasVaoAoEntra` (dois `CreateBrowser()`, ou o mesmo navegador duas vezes sem seguir o redirect) → os dois `Location` no Entra.
  - `Authorize_ComMarcaDeLogout_VaiParaSaiu` (cookie `secco.securegate.signed-out` com o instante atual) → `Location` começa com `/saiu?returnUrl=`.
  - `Authorize_PromptNoneSemSessao_LoginRequired` → `Location` no `redirect_uri` com `error=login_required`.
  - `Authorize_SessaoComStampInvalido_VaiParaLoginNuncaEntra`: login local pelo driver, `UpdateSecurityStampAsync`, novo authorize → `/login`.
  - `Authorize_SessaoDeContaBloqueada_VaiParaLoginNuncaEntra` (`SetLockoutEndDateAsync` no futuro).
  - `Authorize_PromptLoginComSessao_VaiAoEntraComPromptLogin` → `Location` no Entra com `prompt=login`.
  - `Authorize_SessaoValida_EmiteCodeSemPassarPeloEntra` (login local pelo driver e depois o authorize; prova a saída de emergência).
  - `Callback_SemCookieExterno_VoltaParaLoginComReturnUrl` (`GET /login/entra-callback?returnUrl=%2Fconnect%2Fauthorize%3Fx%3D1` → `/login?federatedError=1&returnUrl=%2Fconnect%2Fauthorize%3Fx%3D1`; com `returnUrl=https://evil.example` → exatamente `/login?federatedError=1`).
- [ ] **Passo 2: Rodar e ver falhar** — `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~DirectSignInAuthorizeTests"`.
- [ ] **Passo 3: Implementar** o comportamento acima.
- [ ] **Passo 4: Rodar e ver passar**, junto com `FullyQualifiedName~AuthorizationCodeFlowTests|FullyQualifiedName~MultiTenantLoginTests|FullyQualifiedName~ProductClientInteractiveLoginTests|FullyQualifiedName~TwoFactorLoginTests|FullyQualifiedName~DesignatedOperatorTests` (sem regressão com a opção desligada).
- [ ] **Passo 5: Commit** — `feat(securegate): authorize vai direto ao Entra com a opcao ligada`

---

### Tarefa 6: Logout com "Você saiu"

**Files:**

- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/InteractiveEndpoints.cs:235-244` (`SignInMarkers.MarkSignedOut(context, DateTimeOffset.UtcNow)` antes do `SignOutAsync`)
- Create: `src/SecureGate/Secco.SecureGate.Api/Pages/SignedOut.cshtml` e `SignedOut.cshtml.cs` (`@page "/saiu"`, `Layout = "_AccountLayout"`, `[AllowAnonymous]`)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/DirectSignInAuthorizeTests.cs` (mesma classe e setup da Tarefa 5)

**Interfaces:**

- Consumes: `SignInMarkers`, `FederatedFailureRedirect.IsLocal`.
- Produces: `SignedOutModel` com `string? ReturnUrl`, `void OnGet(string? returnUrl)` e `IActionResult OnPost(string? returnUrl)`. O `OnPost` faz `ClearSignedOut` e `LocalRedirect(IsLocal(returnUrl) ? returnUrl : "/conta")`.

Texto fixo da página: título `Você saiu`, parágrafo `Sua sessão foi encerrada.`, botão `Entrar` (form `POST` com antiforgery e `asp-route-returnUrl`) e link `Entrar com e-mail e senha` para `/login?returnUrl=...`. Usar as classes de `Login.cshtml`.


- [ ] **Passo 1: Testes que falham**
  - `Logout_GravaAMarcaEOProximoAuthorizeVaiParaSaiu`: login local, `GET /connect/logout` (o `Set-Cookie` tem `secco.securegate.signed-out`), depois authorize sem sessão → `/saiu?returnUrl=`.
  - `Saiu_Get_MostraVoceSaiuSemRedirecionar` → `200`, contém `Você saiu` e `Entrar com e-mail e senha`.
  - `Saiu_PostEntrar_ApagaAMarcaEVoltaAoAuthorize` → `302` para o `returnUrl`; o `Set-Cookie` expira a marca; seguir o `Location` leva ao Entra.
  - `Saiu_PostComReturnUrlAbsoluta_VaiParaConta` (`returnUrl=https://evil.example`) → `Location == "/conta"`.
  - `Saiu_PostSemAntiforgery_Recusado` → `400`.
  - `LoginLocal_ApagaAMarcaDeLogout`: com a marca, login pela `/login` → o `Set-Cookie` expira a marca.
  - `Logout_OpcaoDesligada_AuthorizeContinuaIndoParaLogin` (prova "desligado, nada muda" mesmo com a marca).
- [ ] **Passo 2: Rodar e ver falhar.**
- [ ] **Passo 3: Implementar.**
- [ ] **Passo 4: Rodar e ver passar**, mais `FullyQualifiedName~SecurityHeaders` (a página nova herda a CSP com nonce e o `frame-ancestors 'none'`).
- [ ] **Passo 5: Commit** — `feat(securegate): pagina Voce saiu apos o logout no modo direto`

---

### Tarefa 7: Prova por mutação, documentação e verificação final

**Files:**

- Modify: `src/SecureGate/README.md` (seção nova "Login federado direto", depois de "Vínculo com diretório externo (ADR-0026)")
- Modify: `CHANGELOG.md`, `docs/roadmap.md`, `docs/design-decisions-log.md`, `CLAUDE.md` (uma frase no resumo de estado e a versão do `Secco.SecureGate.Client` 0.26.0)

- [ ] **Passo 1: Bateria de mutação.** Aplicar uma mutação por vez, rodar `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~DirectSignIn|FullyQualifiedName~LoginHint|FullyQualifiedName~SignInMarkers|FullyQualifiedName~EntraDirectChallenge|FullyQualifiedName~FederatedFailureRedirect"`, confirmar que ao menos um teste falha e reverter. As mutações:
  1. aplicar o modo direto também no bloco de cookie inválido;
  2. `ResolveAsync` sem exigir `TenantId` no client;
  3. sem exigir `DirectSignIn`;
  4. sem exigir `IsEnabled`;
  5. ignorar `HasDirectAttempt`;
  6. ignorar `HasSignedOut`;
  7. deixar `prompt=none` cair no desafio;
  8. `LoginHint.Sanitize` devolve o valor cru;
  9. `PinDirectory` aceita `http`;
  10. `FederatedFailureRedirect.For` aceita `//`;
  11. `/saiu` sem `IsLocal`;
  12. o reauth no modo direto sem `forceLogin`.

  Registrar quantas foram detectadas (meta: 12 de 12). Se alguma passar, acrescentar o teste que falta na tarefa dona e repetir.

- [ ] **Passo 2: Documentação.**
  - README: o que a opção liga, onde se configura (AdminPortal ou `PUT .../federation` com `directSignIn`), a saída de emergência (`/login`), a `/saiu`, e os **requisitos do lado do cliente** listados na seção "Documentação" da spec. Incluir o aviso de verificar a troca do code contra um tenant real antes de ligar em produção.
  - CHANGELOG: `Secco.SecureGate.Client` 0.26.0 (aditivo: `directSignIn`).
  - Roadmap e CLAUDE.md: uma entrada de incremento pós-Fase 8, com a data 2026-11-18.
  - Log de decisões: `prompt=none` descartado (ganho nulo com o diretório fixado), logout no Entra descartado, e a marca de tentativa na URL em vez de cookie (o motivo da spec).
- [ ] **Passo 3: Verificação completa**
  Run: `dotnet build Secco.Platform.slnx --configuration Release` e depois `dotnet test Secco.Platform.slnx`.
  Expected: build sem warnings; todos os testes verdes. Se uma suíte inteira falhar de uma vez, ver a memória sobre o Ryuk/Testcontainers antes de culpar o código.

- [ ] **Passo 4: Checklist da skill `secco-platform-standards`**, incluindo ADR-0020, contrato regenerado no mesmo commit e ADR-0035 (a marca de logout é estado do navegador, não do servidor).
- [ ] **Passo 5: Commit** — `docs(securegate): login federado direto e requisitos do lado do cliente`
- [ ] **Passo 6: Publicação do `Secco.SecureGate.Client` 0.26.0** — **só com autorização explícita do usuário**, lendo antes a skill `secco-platform-release`.
- [ ] **Passo 7: Aceite manual** (fora do CI, num tenant Entra real e numa máquina ingressada): abrir a intranet leva direto e volta logado; o code emitido pelo endpoint do diretório é aceito na troca pelo `organizations` (se não for, ajustar o endpoint de token no `OnAuthorizationCodeReceived` e voltar à Tarefa 4); sair mostra "Você saiu"; `/login` funciona com a opção ligada.
