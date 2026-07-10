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
- [ ] `AddSeccoPlatform()` — composição de tudo
- [ ] `Secco.SDK.EntityFrameworkCore` — `SeccoNamingConvention` (ADR-0017) + providers SQL Server/PostgreSQL (ADR-0018)
- [ ] Orquestração de seeding — `ReferenceDataSeeder`/`DevelopmentDataSeeder` com guarda dupla (ADR-0019)

## Fase 4 — Migração do LogStream (produto de referência)
- [ ] Mover RS.Logging para `src/LogStream/` com `git mv` (preservar histórico)
- [ ] Renomear `RS.*` → `Secco.LogStream.*` (ADR-0016)
- [ ] Adotar SharedKernel + SDK (remover duplicações locais)
- [ ] Pipeline NSwag: `openapi.json` versionado + `Secco.LogStream.Client` gerado e empacotado
- [ ] Validação de breaking change de contrato no CI (ADR-0006)

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
