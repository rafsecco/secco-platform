# Isolamento de tenant no banco do SecureGate, rodada 2 (RLS) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Row-Level Security no banco do SecureGate nos dois engines, alimentado pelo escopo da rodada 1, com credenciais separadas de `migrate` (dona) e runtime (só leitura/escrita) e recusa de subir com runtime dona fora de Development (issue #32, ADR-0041 camada 2 + emenda).

**Architecture:** `MigrationConnectionString` nova; o migrator usa contexto próprio com ela. Um `DbConnectionInterceptor` grava o escopo como contexto de sessão na abertura (SQL Server `sp_set_session_context ... @read_only = 1`; Postgres `set_config`). Migrations criam funções de predicado + `pol_tenant_isolation` (SQL Server) e políticas com `FORCE ROW LEVEL SECURITY` (Postgres) nas 9 tabelas da rodada 1. A factory de teste roda a suíte inteira com login sem posse.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server + Npgsql), Testcontainers (MsSql + PostgreSql), xUnit + FluentAssertions 7.

**Spec:** `docs/superpowers/specs/2026-10-17-isolamento-de-tenant-rodada-2-design.md` — **ler inteira antes de cada task**. ADR-0041 (com a emenda de 2026-10-17) em `docs/adr/secco-platform-adrs.md`. Rodada 1: `docs/superpowers/specs/2026-10-17-isolamento-de-tenant-rodada-1-design.md`.

## Global Constraints

- Sem contexto de sessão, **nada aparece** (fail-closed) — nas políticas e no interceptor.
- SQL Server: contexto **somente leitura** (`@read_only = 1`); nunca regravar por comando.
- Troca de escopo com a conexão do contexto aberta → `InvalidOperationException` sem dados.
- Política **nunca** afrouxada para fazer teste passar; teste quebrado sob RLS = caminho com escopo errado → corrigir o caminho (e relatar).
- Credencial de runtime: SQL Server `db_datareader` + `db_datawriter`; Postgres sem `BYPASSRLS`, não superusuário, não dono.
- Nomes de banco pela ADR-0017; prefixo novo `pol_` (o revisor atualiza a skill).
- Sem mudança de contrato HTTP (`openapi.json` intacto).
- Build Release sem warnings em `src`; suíte do SecureGate inteira verde; no fim, `dotnet test Secco.Platform.slnx`.
- Commits Conventional Commits direto na `main`; corpo termina com `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- **Nunca** `docker compose down -v`.

## Review Focus

- O `sp_reset_connection` do pool limpa o contexto somente leitura (teste: duas requisições em tenants diferentes reaproveitando conexão).
- Login: plataforma (achar pessoa) → tenant (perfis) em operações separadas — sem transação atravessando a troca.
- Seed de referência sob RLS (runtime) continua funcionando no `migrate` e no F5.
- `Down` das migrations remove política e funções.
- Checagem da subida não roda com credencial de migração.

---

### Task 1: Credenciais separadas e recusa na subida

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/SecureGateDatabaseOptions.cs` (`MigrationConnectionString`), o migrator do SecureGate (`ISeccoDatabaseMigrator` — achar com grep) para usar contexto próprio com essa string (fallback para `ConnectionString`), em escopo de plataforma
- Create: checagem `VerifyRuntimeIsNotOwner` (SQL Server e Postgres, consultas da spec) na subida, ao lado da verificação de migration pendente (ADR-0038); opção `SecureGate:Database:VerifyRuntimeIsNotOwner` (padrão `true`); em Development só `Warning`
- Modify: `docker/sqlserver-init.sql` (dois logins no `secco_securegate`, rebaixamento idempotente do `app`), `docker-compose.yml` (`securegate-migrate` com as duas strings; `securegate-api` só runtime), `src/SecureGate/Secco.SecureGate.Api/appsettings.Development.json`
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Integration/SecureGateApiFactory.cs` — cria o login de runtime sem posse no container e configura as duas strings (migração com `sa`). Conferir também `SelfIssuedAuthSecureGateApiFactory`, `ElevationSecureGateApiFactory`, `PlatformClientsSecureGateApiFactory` e qualquer outra derivada
- Test: `Integration/RuntimeCredentialCheckTests.cs` (runtime `db_owner` fora de Development não sobe; sem posse sobe; em Development só avisa)

- [ ] **Step 1: Failing tests** da checagem.
- [ ] **Step 2: Implement.** A suíte inteira tem que passar com o login sem posse — ainda sem RLS, isso já prova que nenhum caminho de runtime faz DDL.
- [ ] **Step 3:** subir o compose (`docker compose up -d sqlserver sqlserver-init`) e conferir com `sqlcmd` que `secco_securegate_app` não é mais `db_owner` e `secco_securegate_migrator` é. Não use `down -v`.
- [ ] **Step 4: Run** build Release + suíte do SecureGate → PASS.
- [ ] **Step 5: Commit** — `feat(securegate): credenciais separadas de migracao e runtime e recusa de runtime dona do banco (ADR-0041)`.

---

### Task 2: Contexto de sessão e políticas (RLS)

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/Tenancy/TenantSessionContextInterceptor.cs` (SQL Server e Postgres, regra de troca de escopo com conexão aberta) e registro no contexto de runtime (não no de migração)
- Modify: `ITenantAccessScope`/`TenantAccessScope` só se precisar expor versão/evento de mudança (porta continua sem infraestrutura)
- Create: migrations `AddTenantRowLevelSecurity` nos dois engines com o SQL da spec (funções `fn_check_tenant_row`, `fn_check_role_row`, `fn_check_user_row`; política `pol_tenant_isolation` com FILTER + BLOCK no SQL Server; `ENABLE`/`FORCE ROW LEVEL SECURITY` + `CREATE POLICY` no Postgres); `Down` completo
- Test: `Integration/RowLevelSecurityTests.cs` — SQL cru nas 9 tabelas (escopo A, nenhum, plataforma), `INSERT` bloqueado, não-regravação do contexto, troca de escopo com conexão aberta, reaproveitamento de conexão do pool entre tenants

- [ ] **Step 1: Failing tests.**
- [ ] **Step 2: Implement** interceptor + migrations. Rodar a suíte inteira: cada falha nova é um caminho que o RLS bloqueia — corrigir o caminho, nunca a política. Listar cada caso no relatório.
- [ ] **Step 3: Run** build Release + suíte do SecureGate → PASS; `dotnet ef migrations list --no-connect` nos dois projetos.
- [ ] **Step 4: Commit** — `feat(securegate): row-level security no banco do SecureGate com contexto de sessao (ADR-0041)`.

---

### Task 3: Paridade em Postgres

**Files:**
- Create: `tests/SecureGate/Secco.SecureGate.Tests/Integration/PostgresParityTests.cs` no padrão de `tests/LogStream/.../PostgresParityTests.cs` — container Postgres, migrations com papel dono, runtime com papel sem `BYPASSRLS` e não dono; isolamento por SQL cru em `tb_roles`, `tb_users`, `tb_tenant_memberships`; sem contexto nada aparece; `INSERT` de outro tenant recusado (`WITH CHECK`); checagem da subida recusa papel superusuário fora de Development
- Modify: `Secco.SecureGate.Tests.csproj` (`Testcontainers.PostgreSql`, sem versão — CPM)

- [ ] **Step 1: Write tests** e rodar contra o Postgres.
- [ ] **Step 2:** corrigir o SQL Postgres da Task 2 se algo falhar (relatar).
- [ ] **Step 3: Run** suíte do SecureGate → PASS.
- [ ] **Step 4: Commit** — `test(securegate): paridade do row-level security em Postgres (ADR-0041)`.

---

### Depois das tasks (revisor, não subagente)

- Mutação: política `STATE = OFF`; `@read_only = 0`; predicado de usuário sem o ramo do vínculo; `BLOCK PREDICATE` removido; checagem da subida desligada.
- Skill `secco-db-naming`: linha do prefixo `pol_`.
- Nota de upgrade no CHANGELOG (serviço SecureGate), roadmap, log de design, `CLAUDE.md`, README do SecureGate; verificação do F5/compose com os dois logins; fechar a #32.
