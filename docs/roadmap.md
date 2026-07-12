# Secco Platform — Roadmap de Fundação

> Ordem de construção da plataforma. Cada fase só começa quando a anterior está estável.
> Status: `[ ]` pendente · `[~]` em andamento · `[x]` concluída

## Fase 0 — Decisões arquiteturais
- [x] Redigir as ADRs iniciais (`docs/adr/secco-platform-adrs.md`)
- [x] Definir prefixo e marca (`Secco.*`, ADR-0016)
- [x] ADRs de banco de dados: notação húngara (0017), SQL Server default (0018), seed (0019)
- [x] Ratificar ADR-0015 (background processing em camadas: nativo → Hangfire/SQL Server → broker adiado)
- [x] Ratificar as ADRs com status **Proposta** (0001, 0003, 0011, 0014)
- [x] Decidir prefixo de procedures no SQL Server: mantido `sp_` (ADR-0018)

## Fase 1 — Fundação do monorepo
- [x] Criar repositório `secco-platform` e primeiro commit
- [x] `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `nuget.config`, `.gitignore`
- [x] Criar `Secco.Platform.slnx` vazia (formato padrão do .NET 10)
- [x] CI mínimo: build + testes em push/PR (GitHub Actions, `.github/workflows/ci.yml`)

## Fase 2 — Secco.SharedKernel v0.1
- [x] `Result<T>`, `Error` (+ `ErrorType`, `ValidationError`, extensões `Match`/`Map`/`Bind`)
- [x] `PagedResult<T>`, `PageRequest`
  - `ApiResponse<T>`: **adiado** — erros já saem como ProblemDetails e os clients NSwag tipam as respostas; um envelope só entra (Fase 3+) se o SDK provar caso real, conforme regras de admissão da ADR-0003
- [x] `BaseEntity` (Guid v7, igualdade por Id, eventos de domínio), `AuditableEntity` + `ISoftDeletable`, exceções base (`SeccoException`, `DomainInvariantException`), constantes (`SeccoClaims`, `SeccoHeaders`)
- [x] Testes de unidade completos
- [x] Empacotar (NuGet local / GitHub Packages) — workflow `publish-packages.yml` dispara em tag `sharedkernel/v*`; nupkg validado localmente (license, readme, SourceLink, snupkg)

## Fase 3 — Secco.SDK v0.1
- [x] `AddSeccoCorrelation()` — propagação de X-Correlation-Id (`ICorrelationContext`, middleware valida formato do header recebido antes de reaproveitar — ADR-0020; gera Guid v7 caso contrário)
- [x] `AddSeccoTenancy()` — resolução de tenant + `ITenantConnectionFactory` (claim primária, header só sem claim, divergência = 400 — ADR-0020; `ITenantCatalog` com implementação padrão via `IConfiguration`, catálogo SQL chega com o AdminPortal)
- [x] `AddSeccoHealthChecks()` — /health/live (sem checks: liveness = processo responde) e /health/ready (todos os checks; JSON sem descrições/exceções — ADR-0020); `MapSeccoHealthChecks()` nos endpoints
- [x] `AddSeccoResilience()` — standard handler (Polly v8) global via `ConfigureHttpClientDefaults`; retry só em métodos idempotentes (POST/PATCH não repetem até existir ADR de idempotency keys)
- [ ] `AddSeccoAuthorization()` — Role + Permission, resolução em runtime via Secco.SecureGate.Client, cache (tenant_id, role) TTL curto, fail-closed (ADR-0021)
- [x] `AddSeccoPlatform()` — composição de tudo (+ `UseSeccoPlatform()` fixando a ordem correlation → [auth, Fase 6] → tenancy e `MapSeccoPlatform()`; sem toggles, chamada dupla é no-op; auth entra aditiva quando o SecureGate existir)
- [x] `Secco.SDK.EntityFrameworkCore` — `SeccoNamingConvention` (ADR-0017: tabelas via nome do DbSet, colunas por tipo CLR, `pk_`/`fk_`/`uk_`/`idx_` automáticos, `[Column]` explícito vence) + `SeccoDbContext` base; agnóstico de provider — SqlServer/Npgsql ficam nos produtos (ADR-0018)
- [x] Orquestração de seeding — `IReferenceDataSeeder`/`IDevelopmentDataSeeder` + `SeedSeccoDataAsync()` explícito, guarda dupla fail-closed (ADR-0019); Bogus fica na Infrastructure dos produtos

## Fase 4 — Secco.LogStream (produto de referência, reescrito do zero)

> Decisão (2026-07-11): o RS.Logging **não** é migrado — o Secco.LogStream é reescrito do zero
> sobre SharedKernel + SDK, com histórico novo nos padrões da plataforma. O RS.Logging
> (`C:\Programacao\Projects\RS.Logging`) permanece como referência funcional até a paridade.
> Mudanças estruturais da reescrita: tenancy real (database-per-tenant via SDK — sem coluna
> `TenantId`, sem "sem header vê tudo"), Guid v7 (permite ingestão 100% assíncrona inclusive do
> LogProcess pai), nomenclatura via `SeccoNamingConvention`, `Result<T>` + 4 camadas, limites de
> ingestão (ADR-0020), MariaDB fora (ADR-0018).

- [x] 4.1 Fundação: 4 camadas (ADR-0002) + `AddSeccoPlatform()` + OpenAPI/Scalar (DEV) + `openapi.json` versionado com teste de contrato (drift falha o CI; atualização via `SECCO_UPDATE_OPENAPI=true`) + `LogStreamDbContext` por tenant + migrations SQL Server (aplicadas no startup só em DEV) + Testcontainers provando isolamento entre bancos de tenant + CI com path filters (ADR-0014); `ITenantCatalog` ganhou `ListAsync()` (SDK v0.2)
- [x] 4.2 `AddSeccoAuthentication()` no SDK: claims curtas sem remapeamento (ADR-0007, `SeccoClaims.Role` corrigido para `role`), Authority OIDC ou HS256 dev (proibida em Production, fail-fast triplo), `FallbackPolicy` fail-closed (health anônimos explícitos); integrado ao `AddSeccoPlatform()`/`UseSeccoPlatform()` (correlation → auth → tenancy) e aplicado ao LogStream
- [ ] 4.3 Log geral: ingestão assíncrona (bounded channel + `BackgroundService`, `202`), batch com limites, consulta/busca paginada (`PagedResult<T>`, DTOs)
- [ ] 4.4 Log de processos: `LogProcess`/`LogProcessDetail`, ingestão assíncrona (Guid v7 elimina o POST síncrono do pai), auditoria com status agregado
- [ ] 4.5 Log de chamadas de API (`ApiCallLog`): ingestão + consulta/busca
- [ ] 4.6 Retenção: `BackgroundService` (ADR-0015 camada 1) iterando os bancos de tenant via catálogo
- [ ] 4.7 Paridade final: PostgreSQL (migrations + matriz de testes), decisão de full-text (SQL Server `CONTAINS` vs `LIKE`), Dockerfile + compose

## Fase 5 — Secco.Templates
- [ ] Template `dotnet new secco-service` destilado do LogStream
- [ ] Camadas, SDK plugado, OpenAPI + Scalar, client NSwag, testes, Dockerfile, pipeline

## Fase 6 — Secco.SecureGate
- [ ] Nasce do template (prova real do padrão)
- [ ] OIDC provider, JWT, client credentials, catálogo de tenants

## Fase 7 — Secco.AdminPortal
- [ ] Consome os clients de todos os produtos
- [ ] Gestão de tenants, visualização de logs, administração de identidade

## Backlog (só após Fase 7 estável)
NotificationHub · Configuration · FeatureFlags · Audit

---

*Regra de ouro: NotificationHub, Configuration, FeatureFlags e Audit ficam no backlog até o quarteto SharedKernel + SDK + LogStream + SecureGate provar o padrão. Paralelizar sete produtos impede que qualquer um amadureça.*
