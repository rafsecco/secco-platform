# Administração do próprio tenant por client de máquina — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Escopo `securegate:tenant-manage` que deixa um client de máquina vinculado a tenant administrar só o próprio tenant, para a Intranet largar o `securegate:admin`.

**Architecture:** Escopo de client de produto `client_credentials` (molde do `directory:read`, ADR-0045), concedido só pelo operador. Rotas existentes abertas uma a uma por marca própria (`.AllowTenantManage()`) com lista versionada, conferência "tenant da rota = `tenant_id` do token", proteção de conta administrativa e trilha síncrona fail-closed reaproveitando a `TenantAdminAuthority`/`ITenantAdminAuditor` da ADR-0044. Pacote client ganha o registro do client administrativo com o escopo novo; AdminPortal ganha a seção de federação; Intranet adota.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, OpenIddict, EF Core, xUnit + FluentAssertions v7 + NSubstitute + Testcontainers, Blazor Server (AdminPortal), ASP.NET MVC (Intranet).

**Spec:** `docs/superpowers/specs/2026-10-21-administracao-do-proprio-tenant-por-maquina-design.md`

## Global Constraints

- Escopo: `securegate:tenant-manage` (constante `SecureGateScopes.TenantManage`).
- Só client de produto `client_credentials` vinculado a tenant, nunca no tenant de plataforma (`SecureGatePlatform.TenantId`), nunca em client de plataforma nem interativo.
- Só o operador (`securegate:admin`) concede, altera, rotaciona ou exclui client com esse escopo; pela autoridade de admin de tenant → `403` com `SecureGateErrors.Clients.TenantManageRequiresOperator`.
- Rotas abertas (lista exata, nada além): `GetTenant`, `ListUsers`, `GetUser`, `AddUserRole`, `RemoveUserRole`*, `SetUserDisplayName`*, `DeactivateUser`*, `ActivateUser`*, `RevokeUserSessions`*, `CreateRole`, `ListRoles`, `GetRole`, `SetRolePermissions`, `DeleteRole` (* = protege conta administrativa).
- Recusa sempre pela resposta genérica existente `TenantAdminAuthority.Forbidden()` (403 "Acesso negado.").
- Escrita auditada síncrona e fail-closed no LogStream do próprio tenant; ação com prefixo `gestao-do-tenant.`; ator `ActorType.Client`, `ActorId` = `client_id`. Auditor não configurado → escrita `403`, leitura segue. Gravação falhou → `503` (comportamento atual do `AuditAsync`).
- Sem rota nova e sem mudança de `openapi.json` do SecureGate.
- `Secco.SecureGate.Client` 0.21.0 (minor, aditivo). Publicar seguindo a skill `secco-platform-release`.
- Quebra no adotante aceita sem shim (memória `quebra-no-adotante-aceitavel`), com nota de upgrade.
- Commits em Conventional Commits, direto na `main`; build com warnings = erros; `dotnet test` **na solution** antes de cada commit de fechamento de task.

## Review Focus

1. **Escalada por rotação/alteração**: admin de tenant (ADR-0044) rotacionando ou alterando o client que já tem `securegate:tenant-manage` recebe a credencial → tem de dar `403`. Teste na Task 1.
2. **Rota marcada para admin de tenant mas não para tenant-manage**: o machine token chega lá → policy e filtro recusam. Teste na Task 2 (`RotaForaDaLista_Recusa`, com `DeactivateTenant`).
3. **Header `X-Tenant-Id` divergente ou tenant de outro na rota** com token vinculado → `403`/`400`, nunca execução. Teste na Task 2.
4. **Tenant do client desativado depois de emitido o token**: a emissão já recusa token novo (ADR-0037); o token vivo dura até 5 min — aceito, registrado na ADR. Teste de emissão na Task 1.
5. **Tenant sem banco do LogStream no catálogo**: escrita não tem para onde auditar → `503`, nada escrito. Teste na Task 2.

---

### Task 1: Escopo, concessão e guarda contra escalada (núcleo — modelo principal)

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateScopes.cs` (constante + `ClientCredentialsOnlyProductScopes`)
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Seeding/SecureGateReferenceDataSeeder.cs` (registrar o escopo, resource `secco-securegate`, como o `DirectoryRead`)
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Clients/PlatformClientsOptionsValidator.cs` (recusar o escopo em client de plataforma, como `DirectoryRead`)
- Modify: `src/SecureGate/Secco.SecureGate.Application/Clients/ProductClientAccessValidator.cs` (recusar no tenant de plataforma)
- Modify: `src/SecureGate/Secco.SecureGate.Application/Clients/ProductClientHandlers.cs` (`ProductClientTenantAdminGuard`)
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateErrors.cs`
- Modify: `docs/adr/secco-platform-adrs.md` (ADR-0052)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/ProductClientHandlersTests.cs`, `tests/SecureGate/Secco.SecureGate.Tests/Integration/ProductClientTokenTests.cs`, teste do validador de plataforma existente

**Interfaces:**
- Produces: `SecureGateScopes.TenantManage = "securegate:tenant-manage"`; `SecureGateErrors.Clients.TenantManageRequiresOperator` (`Error.Forbidden`, código `SecureGate.Client.TenantManageRequiresOperator`, mensagem "Client com gestão do tenant só é criado ou alterado pelo operador da instalação."); `SecureGateErrors.Clients.TenantManageNotOnPlatformTenant` (`Error.Validation`).
- `ProductClientTenantAdminGuard.RequiresOperatorAsync(IProductClientStore, Guid tenantId, string clientId, CancellationToken) : Task<bool>` — verdadeiro se o client existe e é interativo **ou** tem `TenantManage` nos escopos. Substitui `IsInteractiveAsync` em Rotate/Delete; Update e Create conferem também `command.Scopes`.

- [ ] **Step 1: Escrever a ADR-0052** no fim das ADRs (antes de "Backlog de ADRs futuras"), status Aceita, data 2026-10-21, "Complementa: ADR-0037, ADR-0045; reusa o mecanismo da ADR-0044 (congelada pela ADR-0051)". Contexto/alternativas/decisão/consequências = spec + Global Constraints + Review Focus 4 (até 5 min de token vivo).
- [ ] **Step 2: Testes falhando (unit)** em `ProductClientHandlersTests`:
  - `Criar_ComTenantManagePorAdminDeTenant_RecusaRequerOperador` → `TenantManageRequiresOperator`.
  - `Criar_ComTenantManagePeloOperador_Cria`.
  - `Criar_ComTenantManageNoTenantDePlataforma_Recusa` → `TenantManageNotOnPlatformTenant`.
  - `Criar_InterativoComTenantManage_Recusa` → `ScopeNotAllowed` (já é o caminho de `IsProductScope(..., interactive: true)`).
  - `Alterar_ClientComTenantManagePorAdminDeTenant_Recusa` (existente com o escopo, comando sem ele) e `Alterar_AcrescentandoTenantManagePorAdminDeTenant_Recusa`.
  - `Rotacionar_ClientComTenantManagePorAdminDeTenant_Recusa`, `Excluir_ClientComTenantManagePorAdminDeTenant_Recusa`.
- [ ] **Step 3: Rodar** `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductClientHandlersTests"` → FAIL (constante/erro inexistentes).
- [ ] **Step 4: Implementar** as mudanças listadas em Files. Validador de plataforma: mesma forma e mensagem do `DirectoryRead`, citando ADR-0052.
- [ ] **Step 5: Teste de emissão** em `ProductClientTokenTests`: `Token_ComTenantManage_SaiComTenantId` (operador cria client com o escopo; `/connect/token` devolve token com `tenant_id` do vínculo e o escopo) e `Token_ComTenantManage_TenantDesativado_Recusa`. Teste do validador de plataforma: `ClientDePlataforma_ComTenantManage_FalhaNoStartup`.
- [ ] **Step 6: Rodar** os filtros acima → PASS; depois `dotnet test Secco.Platform.slnx` → verde.
- [ ] **Step 7: Commit** `feat(securegate): escopo securegate:tenant-manage concedido so pelo operador (ADR-0052)`.

### Task 2: Rotas abertas, conferência e trilha (núcleo — modelo principal)

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Api/Authorization/TenantAdminAuthority.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/TenantAdministration/ITenantAdminAuditor.cs` (`UserId` vira `Guid?`)
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/TenantAdministration/LogStreamTenantAdminAuditor.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/UserEndpoints.cs`, `RoleEndpoints.cs`, `TenantEndpoints.cs` (só marcar as 14 rotas)
- Create: `tests/SecureGate/Secco.SecureGate.Tests/Architecture/tenant-manage-routes.txt` (mesmo formato do `tenant-admin-routes.txt`: `operationId | sim|nao`)
- Create: `tests/SecureGate/Secco.SecureGate.Tests/Integration/TenantManageRoutesArchitectureTests.cs` (espelho do `TenantAdminRoutesArchitectureTests`)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/TenantManageAuthorityTests.cs` (coleção `TenantAdminSecureGateApiFactory`, auditor fake)

**Interfaces:**
- Consumes: Task 1 (`SecureGateScopes.TenantManage`).
- Produces:
  - `public sealed record TenantManageAllowedMetadata(bool ProtectsAdminTargets);`
  - `TenantAdminAuthority.AllowTenantManage(this RouteHandlerBuilder, bool protectsAdminTargets = false) : RouteHandlerBuilder` — marca + filtro `AuthorizeTenantManageAsync`.
  - `TenantAdminAuthority.IsTenantManageCaller(ClaimsPrincipal) : bool` = tem `TenantManage` e não tem `Admin` nem `TenantAdmin`.
  - `IsTenantAdminCaller` passa a ser `!Admin && !IsTenantManageCaller`.
  - `AllowsAdminOrMarkedTenantAdmin` admite também `TenantManage` quando o endpoint tem `TenantManageAllowedMetadata`.
  - Filtro da `AllowTenantAdmin`: chamador tenant-manage → `null` se a rota tem `TenantManageAllowedMetadata`, senão `Forbidden()` (defesa em profundidade sobre a policy).
  - `TenantAdminAuditRecord.UserId : Guid?`; `Authority = SecureGateScopes.TenantManage` → auditor grava `ActorType.Client`, `ActorId = ClientId`, prefixo `gestao-do-tenant.`; `ActionPrefix`/`DesignatedOperatorActionPrefix` inalterados.

`AuthorizeTenantManageAsync` (ordem fixa): não é tenant-manage caller → `null`; `tenant_id` do token ausente/inválido, rota sem `tenantId`/`id`, diferentes, ou tenant de plataforma → `Forbidden()`; rota protegida sem `{userId}` ou alvo `IsAdministrativeAccountAsync` → `Forbidden()`; `GET` → `null`; auditor não configurado → `Forbidden()`; senão `AuditAsync` com destino = tenant do token, `administeredTenantId` = mesmo, `targetUserId` da rota, `Authority = TenantManage`.

- [ ] **Step 1: Arquitetura falhando**: criar `tenant-manage-routes.txt` com as 14 linhas e o teste-espelho (rota marcada sem linha reprova; linha sem rota reprova; nenhuma rota com `CreatesTenant`/`AnyGrant`). Rodar → FAIL (nenhuma rota marcada).
- [ ] **Step 2: Integração falhando** em `TenantManageAuthorityTests` (cliente: operador cria client de produto com `securegate:tenant-manage` no tenant A; token via `/connect/token`):
  - `CadaRotaDaLista_NoProprioTenant_Funciona` (Theory pelas 14; escrita registra 1 entrada no auditor fake com `Authority == TenantManage`, `UserId == null`).
  - `OutroTenantNaRota_Recusa403`; `HeaderTenantDivergente_Recusa` (400 do `TenantResolver` ou 403 — assertar "não 2xx e nada escrito").
  - `TenantDePlataformaNaRota_Recusa403`.
  - `AlvoContaAdministrativa_Recusa403` (pessoa com concessão de admin de tenant em A; e o operador demo) para cada rota marcada `sim`.
  - `RotaForaDaLista_Recusa403` (`DeactivateTenant`, `CreateUser`, `UpsertTenantFederation`, `CreateProductClient`, `CreateTenant`).
  - `SemAuditorConfigurado_EscritaRecusa403_LeituraFunciona`.
  - `AuditoriaFalha_Responde503_ENadaEscrito`.
  - `TenantSemLogStreamNoCatalogo_EscritaResponde503` (auditor real não configurado no factory → usar o fake devolvendo `false`; documentar no teste que o destino é sempre o tenant do token).
  - `AdminDeTenant_Continua_Como_Antes` (regressão: uma rota `AllowTenantAdmin` com pessoa concedida segue funcionando).
- [ ] **Step 3: Rodar** os dois filtros → FAIL.
- [ ] **Step 4: Implementar** conforme Interfaces; marcar as 14 rotas com `.AllowTenantManage(protectsAdminTargets: <sim>)` ao lado das marcas existentes.
- [ ] **Step 5: Rodar** os dois filtros → PASS; suíte SecureGate inteira → verde.
- [ ] **Step 6: Mutação** (padrão ADR-0031/0044, mutar à mão, rodar `TenantManageAuthorityTests`, reverter; registrar placar na ADR): (a) remover comparação tenant rota × token; (b) remover recusa do tenant de plataforma; (c) remover proteção de alvo administrativo; (d) policy admitir tenant-manage sem metadata; (e) filtro da `AllowTenantAdmin` deixar passar tenant-manage sem metadata; (f) escrita sem auditoria; (g) auditor gravar `ActorType.User`; (h) guarda da Task 1 em Rotate. Todas precisam ser detectadas; sobrevivente vira teste.
- [ ] **Step 7: Commit** `feat(securegate): rotas da gestao do proprio tenant por maquina, com trilha (ADR-0052)`.

### Task 3: Registro do client no pacote `Secco.SecureGate.Client` (modelo principal)

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Client/Administration/SecureGateAdminClientExtensions.cs`
- Create: `src/SecureGate/Secco.SecureGate.Client/Administration/SecureGateTenantManagementOptions.cs`
- Modify: README e CHANGELOG do pacote
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/` (teste de composição existente do admin client como modelo)

**Interfaces:**
- Produces: `AddSecureGateTenantManagementClient(this IServiceCollection) : IServiceCollection` — registra o mesmo `ISecureGateClient` tipado, `BaseUrl` de `Secco:SecureGate`, credenciais de `Secco:SecureGate:TenantManagement` (`ClientId`, `ClientSecret`), escopo literal `securegate:tenant-manage`, store de token próprio. Mutuamente exclusivo com `AddSecureGateAdminClient()` (o último registrado vence — documentar; a Intranet usa só este).
- `SecureGateTenantManagementOptions { const SectionKey = "Secco:SecureGate:TenantManagement"; string? ClientId; string? ClientSecret; bool IsConfigured; void Validate(); }`.

- [ ] **Step 1: Teste falhando**: `TenantManagementClient_PedeEscopoTenantManage_ComCredencialDaSecao` (handler de client credentials recebe `securegate:tenant-manage` e o `ClientId` da subseção) e `TenantManagementClient_ConfiguracaoParcial_FalhaNoPrimeiroUso`.
- [ ] **Step 2: Implementar**; mesma composição lazy do `AddSecureGateAdminClient`.
- [ ] **Step 3: Rodar** testes → PASS; solution → verde.
- [ ] **Step 4: Commit** `feat(securegate-client): AddSecureGateTenantManagementClient (ADR-0052)`; publicar `Secco.SecureGate.Client` 0.21.0 pela skill `secco-platform-release` (ler a skill antes de taguear).

### Task 4: Seção Federação no AdminPortal (subagente Sonnet)

**Files:**
- Modify: `src/AdminPortal/Secco.AdminPortal/Services/ITenantAdminService.cs`, `SecureGateTenantAdminService.cs`, `AdminModels.cs` (ou `TenantSummary.cs`, onde estiver `TenantDetail`)
- Modify: `src/AdminPortal/Secco.AdminPortal/Components/Pages/TenantManagement.razor`
- Modify: `src/AdminPortal/README.md`
- Test: `tests/AdminPortal/Secco.AdminPortal.Tests/TenantAdminGrantServiceTests.cs` (ou arquivo de serviço de tenant)

**Interfaces:**
- `TenantDetail` ganha `TenantFederationSummary? Federation` (`record TenantFederationSummary(Guid DirectoryId, bool IsEnabled, DateTimeOffset? UpdatedAt)`), preenchido de `client.GetTenantAsync(...).Federation`.
- `ITenantAdminService.SetFederationAsync(Guid tenantId, Guid directoryId, bool enabled, CancellationToken) : Task` → `client.UpsertTenantFederationAsync(tenantId, new UpsertTenantFederationRequest { DirectoryId, Enabled })`.

- [ ] **Step 1: Testes falhando**: `ObterTenant_ProjetaFederacao` (com e sem federação) e `DefinirFederacao_EnviaDirectoryIdEHabilitada`.
- [ ] **Step 2: Implementar** serviço + seção "Federação (Entra ID)" na página do tenant: mostra directory id/estado/atualizado em; formulário com directory id (Guid validado no cliente antes de enviar) e caixa "habilitada"; usa o `RunAsync`/`ApiErrorFormatter` existentes da página.
- [ ] **Step 3: Rodar** `dotnet test tests/AdminPortal/Secco.AdminPortal.Tests` → PASS; solution → verde.
- [ ] **Step 4: Commit** `feat(adminportal): federacao Entra do tenant pelo operador (ADR-0026, ADR-0052)`.

### Task 5: Adoção no `secco-intranet` (subagente Sonnet; repositório `E:\Programacao\Projects\secco-intranet`)

**Files (representativos):**
- Modify: `Directory.Packages.props` (`Secco.SecureGate.Client` 0.21.0)
- Modify: `src/Secco.Intranet.Infrastructure/IntranetInfrastructureExtensions.cs` (`AddSecureGateAdminClient()` → `AddSecureGateTenantManagementClient()`; `credenciais.IsConfigured` passa a considerar a subseção nova para os adapters administrativos)
- Modify: `src/Secco.Intranet.Web/Controllers/AcessoController.cs` + view da aba de federação + `ObterFederacaoHandler`/`DefinirFederacaoHandler`/`IGestaoDeAcesso` (remover federação; manter só leitura? **não** — remover a aba inteira; a tela diz "Federação é configurada pelo operador da instalação no AdminPortal")
- Modify: `appsettings*.json`, `.env.example`, `docker-compose.yml` (subseção `Secco__SecureGate__TenantManagement__ClientId/ClientSecret`)
- Modify: `docs/guia-de-deploy.md` (tabela de clients: client de plataforma **sem** `securegate:admin`; novo client de produto no tenant da Intranet com `securegate:tenant-manage` + `directory:read`, criado pelo operador via `POST /api/v1/tenants/{id}/clients`; auditoria do SecureGate obrigatória para escrita), `README.md`, `docs/plataforma.md` (linha "Atendidas"), `docs/roadmap.md`
- Test: testes existentes de acesso/federação ajustados; `IntranetWebFactory` sem mudança de dublê (porta `IGestaoDeAcesso` igual menos federação)

- [ ] **Step 1:** Remover federação da Intranet (handlers, porta, controller, view, testes) — build e suíte verdes.
- [ ] **Step 2:** Trocar a composição para `AddSecureGateTenantManagementClient()` e a configuração; testes de composição (se existir teste do `ValidacaoSecureGateHostedService`, cobrir a subseção nova: ausente em Production derruba o startup).
- [ ] **Step 3:** Documentação de deploy e nota de upgrade (quebra: criar o client de produto e configurar a subseção antes de subir esta versão).
- [ ] **Step 4:** `dotnet build Secco.Intranet.slnx -c Release` sem avisos; `dotnet test Secco.Intranet.slnx` verde; fumaça `roteiro-fumaca-securegate.md` parte automatizada contra SecureGate local com o client novo.
- [ ] **Step 5: Commit** `feat(acesso): administra o tenant com securegate:tenant-manage, sem securegate:admin (ADR-0052 da plataforma)`.

### Task 6: Fechamento (modelo principal)

- [ ] Roadmap da plataforma (incremento + item "Seção de federação no AdminPortal" sai de "decisões adiadas"), `CLAUDE.md` "Estado atual" (+ versão do client 0.21.0), `docs/design-decisions-log.md`, `docs/getting-started.md`/testing-guide se citarem `securegate:admin` para produto.
- [ ] Spec marcada como implementada no topo.
- [ ] `dotnet build Secco.Platform.slnx --configuration Release` e `dotnet test Secco.Platform.slnx` verdes; revisão de segurança (`/security-review` ou revisão própria pela ADR-0020) do diff inteiro.
- [ ] Commit `docs: ADR-0052 entregue`; push dos dois repositórios após confirmação.
