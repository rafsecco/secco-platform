# Isolamento de tenant no banco do SecureGate, rodada 1 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O isolamento entre tenants no banco do SecureGate deixa de depender de filtro manual: escopo explícito por requisição, filtros globais nomeados no EF (fail-closed), checagem de escrita no `SaveChanges` e teste de arquitetura com allowlist do modo plataforma (issue #32, ADR-0041, camadas 1 e 3).

**Architecture:** `ITenantAccessScope` (porta na Application, implementação scoped na Infrastructure) com estados None/Tenant/Platform e pilha restaurável. O `SecureGateDbContext` lê o escopo nos filtros `"Tenant"`. A borda HTTP entra no escopo do tenant da rota por endpoint filter; os caminhos cross-tenant entram em modo plataforma explicitamente, listados numa allowlist que um teste de arquitetura impõe.

**Tech Stack:** .NET 10, EF Core 10 (filtros nomeados), ASP.NET Core minimal APIs + Razor Pages, ASP.NET Identity, OpenIddict, xUnit + FluentAssertions 7 + NSubstitute + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-10-17-isolamento-de-tenant-rodada-1-design.md` — **ler inteira antes de cada task**. ADR-0041 em `docs/adr/secco-platform-adrs.md`.

## Global Constraints

- ADR-0041 é Aceita. Não negociável:
  - **sem escopo, nada aparece** (fail-closed) — nunca "sem escopo = sem filtro";
  - **`IgnoreQueryFilters` proibido** em todo `src/SecureGate`;
  - o tenant do escopo vem da rota (validada como Guid) ou de valor emitido pelo próprio SecureGate (token/cookie validado) — nunca de corpo, query string ou header;
  - **entrar no tenant sempre que ele for conhecido**; modo plataforma só onde o tenant não existe, com motivo;
  - os filtros manuais existentes **permanecem** (não remover nesta rodada).
- Sem mudança de contrato: `openapi.json` não muda; nenhum pacote publicável muda de API.
- Clean Architecture: a porta não depende de EF/ASP.NET (ADR-0002).
- Logs via `[LoggerMessage]`; o motivo do modo plataforma só em `Debug`.
- Build Release sem warnings em `src`: `dotnet build Secco.Platform.slnx --configuration Release`. Testes: `dotnet test tests/SecureGate/Secco.SecureGate.Tests -c Release` (suíte inteira).
- **Regressão sem afrouxar asserção.** Teste existente quebrado = caminho cross-tenant faltando: corrigir o caminho (escopo de tenant ou entrada em plataforma com motivo), nunca o teste. Relatar cada caso.
- Commits Conventional Commits direto na `main`; corpo termina com `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- **Nunca** `docker compose down -v`.

## Review Focus

- `UserManager.GetRolesAsync` em escopo do tenant B não devolve perfil de A (filtro de `UserRole` pelo tenant do perfil).
- Login: a pessoa é achada em plataforma, mas os perfis do token são lidos em `EnterTenant(loginTenantId)`.
- `Dispose` do escopo restaura o estado anterior em aninhamento (plataforma → tenant → plataforma).
- Contexto de design-time das migrations continua funcionando (`dotnet ef migrations` não pode quebrar).
- Seeds e `migrate` em modo plataforma; o seed de DEV cria dado em vários tenants sem tropeçar na checagem de escrita (plataforma não checa).

---

### Task 1: Escopo, filtros e borda

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Tenancy/ITenantAccessScope.cs` (+ `TenantAccessMode`)
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/Tenancy/TenantAccessScope.cs` (+ registro scoped)
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Contexts/SecureGateDbContext.cs` — construtor recebe o escopo; filtros nomeados `"Tenant"` conforme a tabela da spec; comentário nas entidades fora do filtro; design-time factory (se existir) com escopo em plataforma
- Create: `src/SecureGate/Secco.SecureGate.Api/Tenancy/TenantScopeEndpointFilter.cs` e aplicação nos grupos/rotas da spec (seção "Borda HTTP")
- Create: `src/SecureGate/Secco.SecureGate.Api/Tenancy/PlatformScopePageFilter.cs` (Razor Pages de login e conta) e registro
- Modify: cada ponto da tabela "Rotas sem tenant que entram em modo plataforma" da spec — `/connect/*` (pessoa em plataforma, perfis em `EnterTenant(loginTenantId)` no `LoginTenantResolver`), elevação (concessão em `EnterTenant(user.TenantId)`), federação, catálogo, permission-catalog, session-version, listar/criar tenants, `FindPersonByEmailAsync`, operadores (`EnterTenant(SecureGatePlatform.TenantId)` se couber), seeders, reconciliador, `migrate` e verificação de migration na subida
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/TenantAccessScopeTests.cs`, `Integration/TenantIsolationTests.cs` (itens 1, 2, 3, 5 e 6 da seção Testes da spec)

**Interfaces — Produces:** `ITenantAccessScope { TenantAccessMode Mode; Guid? TenantId; IDisposable EnterTenant(Guid); IDisposable EnterPlatform(string reason); }`.

- [ ] **Step 1: Failing tests** — unitários do escopo; integração de isolamento por entidade (escopo A, nenhum, plataforma), pessoa dona/membro, `GetRolesAsync` em escopo de B, `ExecuteDelete` em escopo de A, e uma rota por grupo com tenant.
- [ ] **Step 2: Implement** escopo + filtros. Rodar a suíte inteira: a quebra em massa esperada (fail-closed) mostra cada caminho sem escopo.
- [ ] **Step 3: Implement** a borda e os pontos de plataforma/tenant até a suíte inteira voltar a passar **sem mudar asserção**. Manter uma lista (para o relatório) de cada `EnterPlatform` criado, com arquivo e motivo.
- [ ] **Step 4: Run** build Release + suíte do SecureGate → PASS. Conferir `dotnet ef migrations list` (ou equivalente) nos dois projetos de migrations.
- [ ] **Step 5: Commit** — `feat(securegate): escopo de acesso e filtro global de tenant no EF (ADR-0041)`.

---

### Task 2: Checagem de escrita e teste de arquitetura

**Files:**
- Modify: `SecureGateDbContext.cs` — override de `SaveChanges`/`SaveChangesAsync` com a regra da seção "Escrita" da spec
- Create: `tests/SecureGate/Secco.SecureGate.Tests/Architecture/TenantIsolationArchitectureTests.cs`
- Create: `tests/SecureGate/Secco.SecureGate.Tests/Architecture/platform-scope-allowlist.txt` (copiar como `None`/`Content` para a saída do teste, ou localizar pela raiz do repositório como outros testes do projeto fazem — seguir o padrão existente)
- Test: casos de escrita em `Integration/TenantIsolationTests.cs` (item 4 da spec)

- [ ] **Step 1: Failing tests** — escrita de entidade de B em escopo A lança; em escopo nenhum lança; em plataforma passa; teste de arquitetura com allowlist vazia reprova listando cada `EnterPlatform` existente.
- [ ] **Step 2: Implement** a checagem de escrita e preencher a allowlist (uma linha por ocorrência, `caminho/relativo.cs | motivo`, motivos curtos e específicos).
- [ ] **Step 3: Prova do teste de arquitetura** — inserir temporariamente um `IgnoreQueryFilters()` e um `EnterPlatform("x")` fora da lista, e remover uma linha da lista sem remover a chamada: os três reprovam. Reverter.
- [ ] **Step 4: Run** build Release + suíte do SecureGate → PASS.
- [ ] **Step 5: Commit** — `feat(securegate): checagem de escrita por tenant e teste de arquitetura do modo plataforma (ADR-0041)`.

---

### Depois das tasks (revisor, não subagente)

- Revisão de segurança do diff e da allowlist (cada `EnterPlatform` justificado; cada um que pudesse ser `EnterTenant`).
- Mutação: remover o filtro de cada entidade **junto** com o filtro manual correspondente; trocar fail-closed por sem filtro; remover a checagem de escrita; remover o endpoint filter de um grupo.
- Suíte inteira da solution; documentação de entrega (roadmap, log de design, `CLAUDE.md`, README do SecureGate); comentário na #32 (rodada 1 entregue, rodada 2 pendente).
