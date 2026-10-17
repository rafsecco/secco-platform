# Isolamento de tenant no banco do SecureGate, rodada 2 (RLS) — design

**Data:** 2026-10-17
**Status:** em revisão
**Decisões arquiteturais:** [ADR-0041](../../adr/secco-platform-adrs.md) (camada 2 desta ADR), [ADR-0038](../../adr/secco-platform-adrs.md) (`migrate` e recusa na subida), [ADR-0018](../../adr/secco-platform-adrs.md) (SQL Server padrão, Postgres segundo), [ADR-0017](../../adr/secco-platform-adrs.md) (nomenclatura), [ADR-0028](../../adr/secco-platform-adrs.md) (modelo de login por banco), [ADR-0020](../../adr/secco-platform-adrs.md)
**Origem:** issue [#32](https://github.com/rafsecco/secco-platform/issues/32). A rodada 1 (escopo explícito, filtro global, teste de arquitetura) está na `main` desde 2026-10-17.

## Contexto

Confirmado no código em 2026-10-17:

- Um único login por banco, **`db_owner`**, para `migrate` e runtime: `docker/sqlserver-init.sql` cria `secco_securegate_app` com `db_owner`; o `docker-compose.yml` usa esse login em `SecureGate__Database__ConnectionString`; o `appsettings.Development.json` usa `sa`.
- `SecureGateDatabaseOptions` tem `Provider` e `ConnectionString`; o `SecureGateDbContext` é registrado por DI com essa string. O verbo `migrate` e o F5 em Development chamam `RunSeccoMigrationsAsync()` (ADR-0038), que usa o contexto de DI.
- O `sqlserver-init` do compose roda a **cada** `up` e é idempotente.
- A factory de teste (`SecureGateApiFactory`) aponta `SecureGate:Database:ConnectionString` para o banco do container, como `sa`.
- O SecureGate não tem teste em Postgres; LogStream e NotificationHub têm `PostgresParityTests` com `Testcontainers.PostgreSql` (já no `Directory.Packages.props`).
- Escopo da rodada 1: `ITenantAccessScope` (nenhum, tenant, plataforma), entradas em plataforma listadas em `platform-scope-allowlist.txt`.

## Decisões (conversa de 2026-10-17)

1. **SQL Server:** contexto de sessão gravado na abertura da conexão com `@read_only = 1` — nem injeção o regrava. Trocar de escopo com conexão aberta (transação em curso) lança: fail-closed.
2. **Postgres:** sem contexto somente leitura; o RLS segura o filtro esquecido em SQL cru, **não** injeção com comandos empilhados. Limite registrado na ADR (emenda) e na nota de upgrade.
3. **A suíte inteira roda com a credencial de runtime real** (login sem posse), mais testes de SQL cru, de não-regravação do contexto e `PostgresParityTests` do SecureGate.
4. (ADR-0041) Duas credenciais: `migrate` dona do banco; runtime só lê e escreve. Fora de Development, recusa subir com runtime dona do banco.
5. Tabelas do OpenIddict seguem fora (coerente com a rodada 1: o pipeline do protocolo consulta o client antes de existir escopo).

## Credenciais

- `SecureGateDatabaseOptions` ganha `MigrationConnectionString`. O migrator do SecureGate (`ISeccoDatabaseMigrator` da ADR-0038) passa a construir **o seu** `SecureGateDbContext` com essa string (opções próprias, escopo em plataforma), em vez do contexto de DI. Sem `MigrationConnectionString`, usa `ConnectionString` (compatível com DEV antigo) — e a checagem da subida decide se isso é aceitável.
- O seed de referência (que roda depois das migrations) usa o contexto de DI, ou seja, a credencial de runtime: é DML, e passa pelo RLS com os escopos que a rodada 1 já definiu.
- **Checagem na subida** (`SecureGate:Database:VerifyRuntimeIsNotOwner`, ligada por padrão, desligada só onde a factory de teste precisar — não deve precisar, ver Testes): fora de Development, recusa subir se a credencial de runtime:
  - SQL Server: for membro de `db_owner` ou `sysadmin`, ou tiver `ALTER ANY SECURITY POLICY`/`CONTROL` no banco (`IS_ROLEMEMBER`, `IS_SRVROLEMEMBER`, `HAS_PERMS_BY_NAME`);
  - Postgres: for superusuário, tiver `BYPASSRLS`, ou for dona de alguma tabela protegida (`pg_roles`, `pg_tables.tableowner`).
  Em Development, só loga `Warning`. Mensagem diz qual configuração ajustar, sem connection string.

## Contexto de sessão

**Chaves:** `secco_mode` (`tenant` | `platform`) e `secco_tenant` (Guid em texto, só com `tenant`). Ausentes = nada aparece.

**SQL Server** — `DbConnectionInterceptor` registrado no `SecureGateDbContext` de runtime:
- `ConnectionOpened(Async)`: lê o `ITenantAccessScope` e executa `sp_set_session_context` para as duas chaves com `@read_only = 1` (em escopo nenhum, não grava nada).
- O escopo ganha um evento/versão de mudança; se ele mudar enquanto a conexão do contexto estiver aberta, a próxima operação lança `InvalidOperationException` ("troca de escopo com conexão aberta", sem dados). O EF abre e fecha a conexão por operação, então o caso normal (plataforma → tenant no login) reabre e regrava.
- O pool chama `sp_reset_connection` ao reaproveitar a conexão, o que limpa o contexto (inclusive o somente leitura).

**Postgres** — mesmo interceptor, executando `select set_config('secco.mode', @m, false), set_config('secco.tenant', @t, false)` na abertura; o Npgsql faz `DISCARD ALL` ao devolver a conexão ao pool. Mesma regra de troca de escopo com conexão aberta.

## Políticas

Criadas por migration (`migrationBuilder.Sql`, um SQL por engine), espelhando os filtros da rodada 1 nas 9 tabelas: `tb_roles`, `tb_role_claims`, `tb_user_roles`, `tb_tenant_memberships`, `tb_users`, `tb_tenant_databases`, `tb_tenant_federations`, `tb_tenant_group_role_mappings`, `tb_elevation_grants`.

**SQL Server:**
- Funções inline com `SCHEMABINDING` (nomes ADR-0017): `fn_check_tenant_row(@id_tenant)`, `fn_check_role_row(@id_role)` (via `tb_roles`), `fn_check_user_row(@id_user, @id_tenant)` (dono ou vínculo).
- Uma política `pol_tenant_isolation` com `FILTER PREDICATE` em cada tabela e `BLOCK PREDICATE` `AFTER INSERT`/`AFTER UPDATE` nas tabelas com `TenantId` direto (a mesma regra da checagem de escrita da rodada 1, agora no banco). `WITH (STATE = ON, SCHEMABINDING = ON)`.
- Prefixo `pol_` é novo: a skill `secco-db-naming` ganha a linha (objeto "security policy").

**Postgres:**
- `ALTER TABLE ... ENABLE ROW LEVEL SECURITY` e `FORCE ROW LEVEL SECURITY` nas 9 tabelas; `CREATE POLICY pol_tenant_isolation ... USING (...) WITH CHECK (...)` lendo `current_setting('secco.mode', true)` e `current_setting('secco.tenant', true)`.

**Down** das migrations remove política e funções.

## Ambiente de desenvolvimento

- `docker/sqlserver-init.sql`: o banco `secco_securegate` passa a ter **dois** logins — `secco_securegate_migrator` (`db_owner`) e `secco_securegate_app` (`db_datareader` + `db_datawriter`, e **removido** de `db_owner` se já for, para o volume existente convergir no próximo `up`). Os outros bancos não mudam.
- `docker-compose.yml`: `securegate-migrate` recebe `SecureGate__Database__MigrationConnectionString` (migrator) e a `ConnectionString` (app, para o seed); `securegate-api` só a do app.
- `appsettings.Development.json`: `ConnectionString` com `secco_securegate_app` e `MigrationConnectionString` com `secco_securegate_migrator` — o F5 passa a exercitar o RLS. Depende do `sqlserver-init` ter rodado (o README de desenvolvimento diz isso).

## Testes

- **Factory:** `SecureGateApiFactory` cria no container, antes de subir o host, o login de runtime sem posse (`db_datareader` + `db_datawriter`) e passa `ConnectionString` com ele e `MigrationConnectionString` com `sa`. A suíte inteira (≈920) passa a rodar sob RLS. Teste quebrado = caminho que o RLS bloqueia: resolver o caminho (escopo certo), nunca afrouxar a política nem o teste.
- **SQL cru** (`Integration/RowLevelSecurityTests.cs`): `FromSqlRaw`/ADO direto em escopo A não vê linha de B em cada uma das 9 tabelas; sem escopo não vê nada; plataforma vê tudo; `INSERT` de linha de B em escopo A é bloqueado pelo banco.
- **Não-regravação:** em conexão aberta em escopo A, `EXEC sp_set_session_context N'secco_mode', N'platform'` falha, e a leitura seguinte continua restrita a A.
- **Troca de escopo com conexão aberta** lança.
- **Checagem da subida:** com runtime `db_owner` fora de Development, o host não sobe; com runtime sem posse, sobe.
- **`PostgresParityTests` do SecureGate:** container Postgres, migrations aplicadas por um papel dono, runtime por um papel sem `BYPASSRLS` e não dono; isolamento por SQL cru em `tb_roles`, `tb_users` e `tb_tenant_memberships`; sem contexto nada aparece.
- **Mutação (revisor):** política desligada (`STATE = OFF`); `@read_only = 0`; predicado do usuário sem o ramo do vínculo; `BLOCK PREDICATE` removido; checagem da subida desligada.

## Nota de upgrade (CHANGELOG, serviço SecureGate)

- **Quebra:** fora de Development o SecureGate não sobe com credencial de runtime dona do banco. Criar um login de migração (dono) e rebaixar o de runtime a leitura e escrita; configurar `SecureGate:Database:MigrationConnectionString` no `migrate`. Script de exemplo na nota (SQL Server e Postgres).
- Rodar o `migrate` da versão nova (cria as políticas).
- Postgres: o RLS segura o filtro esquecido; injeção com comandos empilhados não é contida (sem contexto somente leitura). SQL Server contém as duas.

## Segurança (ADR-0020)

- Injeção no SQL Server em caminho de **tenant** fica presa ao tenant (contexto somente leitura + runtime sem `ALTER`). Em caminho de **plataforma**, não (limite da ADR-0041).
- A credencial de runtime não pode desligar política nem alterar schema.
- O contexto vem do `ITenantAccessScope` (rota validada ou valor emitido pelo SecureGate), nunca de input.
- Sem dependência nova (`Testcontainers.PostgreSql` já é usado no repositório, só em testes).

## Fora desta rodada

RLS nas tabelas do OpenIddict; credencial de runtime por modo (descartada: opção C da conversa); remoção dos filtros manuais redundantes; exportar/excluir dados de um tenant.
