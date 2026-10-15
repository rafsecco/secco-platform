# Pessoa membro de vários tenants — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Uma conta por pessoa, membro de vários tenants; o tenant do login vem da aplicação (client interativo vinculado a tenant); perfis no token só do tenant do login; vínculos geridos por API e pelo AdminPortal (issue #35, ADR-0040).

**Architecture:** Tabela `tb_tenant_memberships` (pessoa, tenant, ativo). `User.TenantId` vira o tenant **dono**. A emissão de token (authorize, troca do code, renovação) resolve o tenant do login pelo client ou pelo token emitido, exige vínculo ativo e filtra os perfis por esse tenant. Rotas de vínculo em `/api/v1/tenants/{tenantId}/members`. A API de clients do tenant aceita `authorization_code`.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server + PostgreSQL), OpenIddict, ASP.NET Core Identity, minimal APIs, Blazor Server (AdminPortal), NSwag, xUnit + FluentAssertions 7 + NSubstitute + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-10-15-pessoa-em-varios-tenants-design.md` — **ler inteira antes de cada task**. ADR-0040 em `docs/adr/secco-platform-adrs.md`.

**Referência (não é base):** a branch `spike/35-varios-tenants` (commit `4202c71`) tem um protótipo do caminho de emissão e um teste de integração de ponta a ponta (`MultiTenantMembershipSpikeTests.cs`). Pode ser lida com `git show 4202c71:<caminho>`; **não** mesclar nem copiar o atalho do vínculo em `tb_user_claims`.

## Global Constraints

- ADR-0040 é Aceita. Invariantes que não se negociam:
  - perfis no token **só** do tenant do login (`role.TenantId == loginTenantId`); `userManager.GetRolesAsync` não é usado em caminho de emissão;
  - operador de instalação = tenant do login é o de plataforma **e** `user.TenantId` é o de plataforma **e** tem o perfil;
  - o tenant do login vem do registro do client (`OidcApplication.TenantId`) ou do token que o OpenIddict validou — **nunca** de parâmetro da requisição;
  - operações de pessoa (convite, reset de senha, login local, nome de exibição pelo admin, reset do segundo fator, login externo, ativar/desativar, encerrar sessões, elevação) continuam só pelo tenant dono (`user.TenantId == tenantId`), respondendo `404` pelos outros.
- Records posicionais consumidos pelo NSwag (`UserDto`, `UserDetailDto`, `ProductClientCommand`, `ProductClientDto`, `CreatedProductClientDto`): campos novos **só no fim**, opcionais.
- `operationId` novos exatos: `AddTenantMember`, `SetTenantMember`, `SuspendTenantMember`, `RemoveTenantMember`. Nenhum `operationId` existente muda.
- Banco por convention (ADR-0017); migration nos dois engines.
- Clean Architecture: nada de EF/Identity/OpenIddict na Application; `Result` para erro de negócio (ADR-0004).
- Logs via `[LoggerMessage]`, sem e-mail em log.
- Build Release sem warnings: `dotnet build Secco.Platform.slnx --configuration Release`. Testes do SecureGate: `dotnet test tests/SecureGate/Secco.SecureGate.Tests -c Release`. Suíte inteira no fim: `dotnet test Secco.Platform.slnx`.
- Commits Conventional Commits direto na `main`; corpo termina com `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- **Nunca** rodar `docker compose down -v`.

## Review Focus

- Homônimo do perfil de operador num tenant de que o operador é membro: token comum (Task 2).
- Remoção do vínculo apaga os perfis da pessoa **naquele** tenant e só nele (Task 3).
- `ListUsers` com pessoa que tem perfis em dois tenants: só os do tenant da rota (Task 3).
- `DELETE` de vínculo inexistente é `204`, de pessoa inexistente é `404` (Task 3).
- Renovação: refresh token emitido para o tenant B, vínculo de B removido → `invalid_grant`, mesmo com vínculo ativo em A (Task 2).
- Client interativo com redirect `http://` fora de Development recusado (Task 4).

---

### Task 1: Vínculo — entidade, tabela, migration, repositório

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Domain/Tenants/TenantMembership.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Tenants/ITenantMembershipRepository.cs`
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/Tenants/TenantMembershipRepository.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Contexts/SecureGateDbContext.cs` (DbSet + mapeamento: PK `(UserId, TenantId)`, FK `tb_users` cascade, FK `tb_tenants` restrict, índice por `TenantId`)
- Modify: registro de DI da Infrastructure (onde os outros repositórios são registrados)
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Users/UserAccountService.cs` — `CreateAsync` cria o vínculo do dono na mesma `SaveChanges`
- Modify: `SecureGateDevelopmentDataSeeder` e `SecureGateReferenceDataSeeder` — todo usuário que eles criam ganha o vínculo do dono (conferir onde criam usuário)
- Create: migrations `AddTenantMemberships` nos dois engines, **com** o `INSERT ... SELECT` do vínculo do dono para todo usuário existente (`migrationBuilder.Sql`, um SQL por engine, `dt_created_at` = agora)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/TenantMembershipTests.cs`; `Integration/TenantMembershipSchemaTests.cs` (ou dentro de `PlatformSchemaTests`)

**Interfaces — Produces:**
- `TenantMembership(Guid userId, Guid tenantId, DateTimeOffset createdAt)` — `UserId`, `TenantId`, `IsActive` (nasce `true`), `CreatedAt`, `void Suspend()`, `void Reactivate()`; ids vazios → `ArgumentException`.
- `ITenantMembershipRepository`: `Task<TenantMembership?> GetAsync(Guid tenantId, Guid userId, CancellationToken)`, `Task<bool> IsActiveMemberAsync(Guid tenantId, Guid userId, CancellationToken)`, `void Add(TenantMembership)`, `Task<bool> RemoveAsync(Guid tenantId, Guid userId, CancellationToken)` (apaga o vínculo **e** as linhas de `tb_user_roles` da pessoa cujo `role.TenantId == tenantId`, numa transação; devolve se havia vínculo), `Task SaveChangesAsync(CancellationToken)`.

- [ ] **Step 1: Failing tests** — unitários da entidade; integração: criar usuário pelo `IUserDirectory.CreateAsync` gera vínculo ativo do dono; PK impede vínculo duplicado; `RemoveAsync` apaga perfis só do tenant do vínculo (pessoa com perfil em A e B, remover B mantém o de A); migration aplica do zero (`Migrations_Always_ApplyFromScratch` existente).
- [ ] **Step 2: Implement** e gerar migrations:

```bash
dotnet ef migrations add AddTenantMemberships --project src/SecureGate/Secco.SecureGate.Migrations.SqlServer/Secco.SecureGate.Migrations.SqlServer.csproj --output-dir Migrations
dotnet ef migrations add AddTenantMemberships --project src/SecureGate/Secco.SecureGate.Migrations.Postgres/Secco.SecureGate.Migrations.Postgres.csproj --output-dir Migrations
```

  Acrescentar à mão, no `Up` de cada uma, depois do `CreateTable`, o `INSERT` do vínculo do dono (nomes de coluna conferidos no `Designer` gerado; Postgres com aspas duplas, como nas migrations existentes de dado, se houver).
- [ ] **Step 3: Teste de migration com dado:** um teste que aplica as migrations até a anterior, insere um usuário cru, aplica `AddTenantMemberships` e confere o vínculo (seguir o padrão de teste de migration existente, se houver; se não houver mecanismo de "migrar até X" nas factories, usar `IMigrator.MigrateAsync("<anterior>")` e depois `MigrateAsync()`).
- [ ] **Step 4: Run** testes do SecureGate → PASS (a suíte inteira do projeto, não só os novos).
- [ ] **Step 5: Commit** — `feat(securegate): vinculo pessoa-tenant com migration do vinculo do dono (ADR-0040)`.

---

### Task 2: Emissão de token pelo tenant do login

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Api/Identity/LoginTenantResolver.cs`
- Modify: `Api/Identity/InstallationOperatorPolicy.cs`, `OperatorTwoFactorPolicy.cs`, `OidcPrincipalBuilder.cs`, `AccountStateGuard.cs` (este **não** muda de regra: continua conferindo o tenant dono)
- Modify: `Api/Endpoints/InteractiveEndpoints.cs`, `Api/Endpoints/TokenEndpoints.cs` (`HandleUserGrantAsync`; `HandleElevationAsync` só se o compilador exigir — a elevação continua no dono)
- Modify: `Api/Pages/Account/TwoFactor.cshtml.cs` (perfis do tenant dono)
- Modify: todos os outros chamadores de `IsInstallationOperator`/`FilterScopes`/`RequiresEnrollment`/`CanDisable` (achar com grep) passando o tenant explícito
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/MultiTenantLoginTests.cs`

**Interfaces — Produces:**
- `LoginTenantResolver` (classe registrada no DI, scoped) com:
  - `Task<Guid> ForAuthorizeAsync(string? clientId, User user, CancellationToken)` — `OidcApplication.TenantId` do client ou `user.TenantId`;
  - `Guid ForRenewal(ClaimsPrincipal stored, User user)` — `tenant_id` do principal validado, ou `user.TenantId` sem a claim;
  - `Task<bool> CanIssueAsync(User user, Guid loginTenantId, CancellationToken)` — `loginTenantId == user.TenantId` (dono: sempre membro, pela coluna — não consultar a linha do dono) **ou** (vínculo ativo via `ITenantMembershipRepository.IsActiveMemberAsync` **e** tenant do login ativo via `ITenantRepository`);
  - `Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, Guid loginTenantId, CancellationToken)` — perfis com `role.TenantId == loginTenantId` (porta nova no `IUserDirectory` ou consulta própria via repositório; não `UserManager`).
- `InstallationOperatorPolicy.IsInstallationOperator(User user, IEnumerable<string> roles, Guid loginTenantId)` — **sem** default; `FilterScopes(..., Guid loginTenantId)`; `OperatorTwoFactorPolicy.RequiresEnrollment(..., Guid loginTenantId)`; `CanDisable(user, roles)` passa `user.TenantId`.
- `OidcPrincipalBuilder.ForUser(User user, IEnumerable<string> roles, IEnumerable<string> scopes, IEnumerable<string> resources, Guid loginTenantId)`.

Ordem no authorize: autenticar cookie → `ValidateSecurityStampAsync` → `AccountStateGuard` (falha: SignOut + Challenge, como hoje) → tenant do login → `CanIssueAsync` (falha: `Results.Forbid` com `Errors.AccessDenied` e "A conta não tem acesso a esta aplicação.", **sem** SignOut) → perfis do tenant → 2FA do operador → scopes → principal.

Na renovação: `AccountStateGuard` → tenant do token → `CanIssueAsync` → perfis → scopes → principal; qualquer falha → `invalid_grant` "A conta não pode mais ser autenticada.".

- [ ] **Step 1: Failing tests** (`MultiTenantLoginTests`, collection `SelfIssuedApiCollectionDefinition`, usando `OidcLoginDriver`; criar clients vinculados direto no banco como o spike faz — origem `Api` + `TenantId` —, porque a API de client interativo é da Task 4). Cobrir os itens 1–8 da seção Testes da spec: SSO com tenant e perfis de cada aplicação; perfil de outro tenant nunca no token; operador com homônimo; sem vínculo → `access_denied` e sessão viva; vínculo suspenso/removido → renovação `invalid_grant` (com vínculo ativo no outro tenant); tenant do login desativado; tenant dono desativado; renovação mantém o tenant.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Run** a suíte inteira do SecureGate → PASS **sem alterar testes existentes de login** (é a prova de que um sistema só não muda; se algum existente quebrar, parar e reportar em vez de ajustar o teste).
- [ ] **Step 4: Commit** — `feat(securegate): tenant do login pela aplicacao e perfis so do tenant do login (ADR-0040)`.

---

### Task 3: Gestão de vínculos e rotas de usuário

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Members/` — `TenantMemberDto.cs`, `AddTenantMemberHandler.cs`, `SetTenantMemberHandler.cs`, `SuspendTenantMemberHandler.cs`, `RemoveTenantMemberHandler.cs`
- Create: `src/SecureGate/Secco.SecureGate.Api/Endpoints/MembershipEndpoints.cs` (+ `app.MapMembershipEndpoints()` no `Program.cs`, + registro dos handlers onde os outros são registrados)
- Modify: `Application/SecureGateErrors.cs` — classe `Members` com `UserNotFound` (404), `AlreadyMember` (409), `OwnerMembership` (409), `PlatformTenantForeignUser` (409), `CannotChangeOwnMembership` (409), `EmailInvalid` (400), `NotMember` (409), seguindo o padrão de códigos/mensagens existente
- Modify: `Application/Sessions/ISessionRevoker.cs` — `SessionRevocationReason.MembershipEnded` (no fim do enum); conferir consumidores do enum (auditoria/log) e tratar o valor novo
- Modify: `Application/Users/UserDto.cs` (+ `string? Membership = null, Guid? OwnerTenantId = null` no fim), `UserDetailDto.cs` (+ `string? Membership = null` no fim), `IUserDirectory.cs` e `Infrastructure/Users/UserAccountService.cs`:
  - `ListByTenantAsync`: dono + membros (join com `tb_tenant_memberships`), perfis filtrados por `role.TenantId == tenantId`, `Membership` = `Owner` | `Member` | `Suspended`;
  - `GetAsync`: aceita membro; para não dono, `HasPassword`/`LocalLoginEnabled`/`TwoFactorEnabled` = `false` e `ExternalLogins` vazio;
  - `AddRoleAsync`/`RemoveRoleAsync`: exigem vínculo (ativo ou suspenso) no tenant — sem vínculo, outcome novo `NotMember` → `409 Members.NotMember` (mapear em `AddUserRoleHandler.ToResult`);
  - `BelongsToTenantAsync`, `SetActiveAsync`, `RemoveExternalLoginAsync`, `SetDisplayNameAsync`, `HasRoleAsync`: **inalterados** (dono). Acrescentar um comentário curto em `BelongsToTenantAsync` dizendo que é "dono", porque o nome passou a ser ambíguo.
  - novo `Task<Guid?> FindIdByEmailAsync(string normalizedEmail, CancellationToken)` e `Task<Guid?> GetOwnerTenantAsync(Guid userId, CancellationToken)` (ou equivalente) para os handlers de vínculo.
- Test: `Integration/TenantMembershipApiTests.cs`, `Integration/UserRoutesForMembersTests.cs`; unitários dos handlers se o padrão do projeto tiver (`Unit/`).

**Regras dos handlers** (spec, seção "Gestão de vínculos"): tenant inexistente `404`; tenant inativo aceita; plataforma não aceita pessoa de outro dono; dono não é suspenso nem removido; quem chama não muda o próprio vínculo (`CallerSubject` vindo da claim `sub`, como `RemoveUserRoleCommand`); suspender/remover **de fato** chama `ISessionRevoker.RevokeAllAsync(userId, MembershipEnded)`; chamadas idempotentes sem efeito não revogam. `PUT` inclui ou reativa (`204`). `POST` por e-mail: `201` + `TenantMemberDto` com `Location` no padrão das outras criações; `409` se já membro (ativo ou suspenso). E-mail validado como em `CreateUserHandler`.

- [ ] **Step 1: Failing tests** — itens 9–12 da spec: uma chamada por rota de pessoa num tenant não dono → `404` (convite, reset de senha, login local, nome de exibição, reset 2FA, login externo, desativar, ativar, encerrar sessões, elevação); todos os `409`/`404`/`204` das rotas de vínculo; `DELETE` apaga perfis do tenant e reincluir não devolve; suspender revoga sessão (`sver` muda pelo endpoint de versão de sessão); atribuir perfil a não membro → `409`; `ListUsers` só com perfis do tenant da rota; `GetUser` de membro com campos de credencial vazios.
- [ ] **Step 2: Implement** (endpoints com `.WithName(...)`, `.RequireAuthorization` igual ao grupo de usuários, `Produces` documentando os códigos).
- [ ] **Step 3: Run** suíte do SecureGate → PASS.
- [ ] **Step 4: Commit** — `feat(securegate): gestao de vinculos por tenant e rotas de usuario para membros (ADR-0040)`.

---

### Task 4: Client interativo pela API do tenant

**Files:**
- Modify: `Application/Clients/ProductClientDtos.cs` (campos no fim: `string? Type = null`, `IReadOnlyList<string>? RedirectUris = null`, `IReadOnlyList<string>? PostLogoutRedirectUris = null` em `ProductClientCommand`, `ProductClientDto`, `CreatedProductClientDto`; `ProductClientAccess` ganha o tipo e as URIs)
- Modify: `Application/Clients/ProductClientAccessValidator.cs`, `ProductClientRules.cs` (constantes `ClientCredentialsType = "client_credentials"`, `AuthorizationCodeType = "authorization_code"`, `MaxRedirectUris = 10`, conjunto dos escopos OIDC padrão), `ProductClientHandlers.cs`
- Modify: `Infrastructure/Clients/OpenIddictProductClientStore.cs` (permissões, requisito PKCE, consent implícito, redirect URIs; leitura devolve tipo e URIs)
- Modify: onde a regra "HTTPS fora de Development" da configuração de plataforma vive (reusar, não duplicar; se for privada do validador da configuração, extrair para um helper comum)
- Test: `Integration/ProductClientInteractiveTests.cs`

Regras (spec, seção "Client interativo"): tipo nulo = `client_credentials` (comportamento de hoje intacto); `authorization_code` confidencial + PKCE + consent implícito; escopos de produto + `openid profile email roles offline_access`; 1–10 redirect URIs absolutas sem fragmento, HTTPS fora de Development; post-logout opcional com a mesma regra; papéis de máquina recusados (`400`); `Type` imutável no `PUT` (`400`); `client_credentials` com redirect URIs → `400`.

- [ ] **Step 1: Failing tests** — item 13 da spec, incluindo um login de ponta a ponta com o client criado pela API (driver + PKCE + secret na troca do code) emitindo o tenant do vínculo; e os testes existentes de client de produto continuam passando sem mudança.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Run** suíte do SecureGate → PASS.
- [ ] **Step 4: Commit** — `feat(securegate): client interativo vinculado a tenant pela API (ADR-0040)`.

---

### Task 5: Contrato, client NSwag e AdminPortal

**Files:**
- Modify: snapshot `src/SecureGate/Secco.SecureGate.Api/openapi/openapi.json` (via `SECCO_UPDATE_OPENAPI=true` no `OpenApiContractTests`)
- Modify: `src/SecureGate/Secco.SecureGate.Client/README.md` (métodos de vínculo, client interativo)
- Modify: AdminPortal — `Services/IUserAdminService.cs` (+ vínculos), modelos de tela, `Components/Pages/TenantManagement.razor` (coluna de vínculo na lista de usuários + seção **Membros**: incluir por e-mail, suspender, reativar, remover, com confirmação avisando que as sessões da pessoa são encerradas), `Components/Pages/UserManagement.razor` (esconde ações de pessoa quando `Membership != Owner`, mostra o tenant dono)
- Test: testes do AdminPortal no padrão existente (bUnit/serviço), se houver para essas telas

- [ ] **Step 1:** regenerar o snapshot; conferir no diff que entraram só os 4 `operationId` novos, `TenantMemberDto`, os campos novos no fim dos records e o corpo de `AddTenantMember`; nenhum `operationId` existente mudou.
- [ ] **Step 2:** build do client e do AdminPortal; implementar as telas seguindo o estilo das seções existentes (mesmo tratamento de erro do `ISecureGateClientFactory`, mesmas mensagens traduzidas de ProblemDetails).
- [ ] **Step 3: Run** `dotnet build Secco.Platform.slnx --configuration Release` e `dotnet test Secco.Platform.slnx` → PASS.
- [ ] **Step 4: Commit** — `feat(adminportal): membros do tenant e vinculo na lista de usuarios (ADR-0040)`.

---

### Depois das tasks (revisor, não subagente)

- Revisão de segurança do diff inteiro e testes de mutação das invariantes (homônimo de perfil, homônimo de operador, filtro de perfis, tenant da renovação, operação de pessoa por tenant não dono, remoção apaga perfis, plataforma sem membro de fora).
- Documentação de entrega: CHANGELOG (`Secco.SecureGate.Client` 0.17.0), README do SecureGate, `docs/roadmap.md` (#35 entregue), `docs/design-decisions-log.md`, `CLAUDE.md`, nota de adoção para a Intranet (dois clients interativos).
- Verificação no navegador do AdminPortal (seção Membros) e do login multi-tenant.
