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
- [x] `AddSeccoAuthorization()` — Role + Permission, resolução em runtime via Secco.SecureGate.Client, cache (tenant_id, role) TTL curto, fail-closed (ADR-0021) — **entregue na Fase 6.4**, incluído no `AddSeccoPlatform()`
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
- [x] 4.3 Log geral: `LogEntry` (Guid v7), ingestão assíncrona (bounded channel + worker que restaura o tenant via `SetTenant` do SDK; fila cheia = 503 + Retry-After), batch com limites (ADR-0020, perfil balanceado configurável), consulta/busca paginada; `Secco.LogStream.Client` NSwag nasceu (gerado no build a partir do snapshot); kernel ganhou `ErrorType.Unavailable` e o SDK ganhou `ToHttpResult()` (Result → ProblemDetails)
- [x] 4.4 Log de processos: `LogProcess`/`LogProcessDetail` (details aninhados na rota; `Name` + `ExternalReference`), ingestão assíncrona também do pai (Guid v7 — fila FIFO única garante pai antes dos details), auditoria embutida na listagem (status agregado computado no SQL via `MAX(ie_level)`, filtrável por `?status=`; regra pura `ProcessStatusRule` no Domain)
- [x] 4.5 Log de chamadas de API (`ApiCallLog`): ingestão assíncrona + consulta/busca; sanitização server-side de headers (blocklist embutida + configurável → `[REDACTED]`, ADR-0020), bodies opcionais truncados em 64 KB, validação de URL/método/status
- [x] 4.6 Retenção: `BackgroundService` + `PeriodicTimer` (ADR-0015 camada 1) iterando os bancos via `ITenantCatalog.ListAsync()`; **opt-in explícito** (sem `DefaultDays` = inativo; config inválida = inativo, fail-safe), janela única com override por tenant (`LogStream:Retention`), `ExecuteDelete` com cascade nos details
- [x] 4.7 Paridade final: PostgreSQL como segundo provider (assemblies de migrations separados por engine — ADR-0018; seleção via `LogStream:Database:Provider`; paridade provada por testes: migrations do zero + schema minúsculo sem aspas + E2E), full-text decidido (**`LIKE` na v1; full-text por provider vai ao backlog** com demanda real), Dockerfile multi-stage + docker-compose de desenvolvimento; options do produto passaram a bind lazy via DI

**Fase 4 concluída** — paridade funcional com o RS.Logging atingida, com as melhorias estruturais registradas acima. Desvios conscientes: MariaDB fora (ADR-0018), `TraceId` manual substituído por OpenTelemetry futuro (ADR-0008), full-text/webhook/dashboard no backlog.

## Fase 5 — Secco.Templates
- [x] Template `dotnet new secco-service` destilado do LogStream (`templates/secco-service`; sourceName `Secco.SampleService` + símbolo derivado para nomes curtos; **monorepo-first**: ProjectReference — variante NuGet quando houver adotante externo)
- [x] Camadas, SDK plugado, OpenAPI + Scalar, client NSwag, migrations por engine, testes (unit + Testcontainers + contrato), Dockerfile/compose — com recurso **Sample** completo como referência executável (apagável); pacote `Secco.Templates` (tag `templates/v*`); job `validate-template` no CI instancia + gera migrations + builda + testa o produto gerado quando template OU plataforma mudam (ADR-0013: divergência é bug)

## Fase 6 — Secco.SecureGate

> Arquitetura na ADR-0022: OpenIddict + ASP.NET Identity, identidade como dado de plataforma
> (banco próprio `secco_securegate` com usuários/roles/permissions por tenant, clients OIDC e
> catálogo de tenants); client credentials primeiro.

- [x] 6.1 Nasceu do template (prova real do padrão — inclusive achou e corrigiu um bug do template: descoberta do snapshot pós-mudança dos testes) + banco de plataforma `secco_securegate`: Identity + OpenIddict (entidades próprias com nomes curtos e chave Guid) + migrations nos 2 engines com **ADR-0017 completa** (`tb_users`, `id_pk_user`, `fl_email_confirmed`, `id_pfk_*` na associativa, `tb_oidc_*` — provado por testes de schema), `Tenant` do catálogo com FKs de users/roles, role único por tenant (ADR-0021); Client NSwag fora da solution até o 1º contrato (6.3)
- [x] 6.2 Client credentials + JWKS/discovery: OpenIddict server (`/connect/token` passthrough, JWT puro sem criptografia de conteúdo — produtos validam com `JwtBearer` padrão), certificados por ambiente (dev automático; **Production sem certificado não sobe**), scopes por produto via **seed de referência** (estreia da ADR-0019) + client/tenant demo em seed de DEV; **E2E cross-produto provado**: LogStream aceita token emitido pelo SecureGate via JWKS (tenant via `X-Tenant-Id`, o cenário interno da ADR-0005) e rejeita token forjado
- [x] 6.3 Catálogo de tenants servido pelo SecureGate: `TenantDatabase` por (tenant, produto) — database-per-tenant É por produto (ADR-0005); API de gestão write-only para segredos (scope `securegate:admin`) + API de catálogo com **scope por produto** (`catalog:<produto>`, least privilege ADR-0020 — a única superfície que entrega connection strings); `Secco.SecureGate.Client` nasceu (NSwag + `SecureGateTenantCatalog : ITenantCatalog` com client credentials automático, cache TTL curto e **stale em falha**; decisão lazy por configuração — sem a seção `Secco:SecureGate`, DEV segue no catálogo por configuração; parcial = fail-fast); SDK ganhou o middleware de exceções de tenancy (tenant desconhecido → 400, catálogo indisponível → **503 + Retry-After**); LogStream adotou e o E2E prova: **zero tenants em configuração** — migrations e requests resolvem pelo SecureGate, e desativar tenant propaga em ≤ 1 TTL
- [x] 6.4 `AddSeccoAuthorization()` (ADR-0021): policies dinâmicas por permissão (`RequireAuthorization("recurso:acao")` — formato canônico validado pelo `SeccoPermissions` do kernel; **constantes vivem em cada produto**, desvio consciente da letra da ADR-0021 pela regra de admissão da ADR-0003), cache `(tenant_id, role)` TTL curto **estrito e fail-closed** (sem stale — autorização nunca falha aberta), resolver por configuração em DEV/testes e remoto via `Secco.SecureGate.Client` (scope único `authorization:read`); **clients OIDC ganharam roles** (`ds_roles` — máquinas usam o MESMO modelo Role+Permission dos usuários, claim curta `role` no client credentials); SecureGate ganhou gestão de roles/permissões por tenant (PUT idempotente, scope admin) e endpoint de resolução; pipeline reordenado (tenancy ANTES de authorization — o tenant precisa existir quando as policies avaliam); LogStream adotou (`log-entries|log-processes|api-call-logs : read|write`) e o E2E prova: **revogar permissão propaga em ≤ 1 TTL com o mesmo token ainda válido** — a razão de ser da ADR-0021
- [x] 6.5 Login de usuário: **authorization code + PKCE obrigatório** + refresh token (ADR-0022). ASP.NET Identity via `AddIdentityCore` com cookie **não-default** (o padrão da API segue JwtBearer — ADR-0007) + telas Razor (Login, self-contained, antiforgery, sem assets externos); endpoints `/connect/authorize` (desafia o cookie → login → emite o code), `/connect/userinfo`, `/connect/logout` (end-session) — `TokenEndpoints` estendido para authorization_code/refresh **re-derivando claims do banco a cada emissão** (usuário desativado/role alterado reflete no refresh, ADR-0020); email/username **único global**, tenant vem do registro (decisão da 6.1 — sem seletor de tenant); usuários **provisionados por admin** (`POST /api/v1/tenants/{id}/users`, scope `securegate:admin`, hash do Identity, sem auto-registro; senha nunca volta) + roles atribuídos por tenant; consent **implícito** para first-party (tela adiada até client de terceiros — YAGNI); seed de DEV ganhou usuário demo + client web (PKCE); E2E prova o fluxo real do navegador (desafio → login antiforgery → code no redirect → troca com `code_verifier` → claims curtas no access token → userinfo → refresh; verifier errado = 400)

**Fase 6 concluída** — SecureGate cobre client credentials (máquinas), catálogo de tenants, autorização Role+Permission e login de usuário. O quarteto SharedKernel + SDK + LogStream + SecureGate provou o padrão da plataforma.

## Fase 7 — Secco.AdminPortal

> Arquitetura na ADR-0023: **Blazor Server** como relying party OIDC (não é produto de 4
> camadas — sem domínio/banco próprios); autentica via authorization code + PKCE (Fase 6.5)
> e chama os produtos **on-behalf-of** o operador (token do operador via clients NSwag);
> operador **cross-tenant** (role `platform-operator` no tenant de plataforma).

- [x] 7.1 Fundação: projeto Blazor Server + login OIDC (cookie + code/PKCE contra o SecureGate, `SaveTokens` como claim custodiada no cookie) + shell autenticado (layout, navegação, logout, badge do usuário) + gate de operador (policy `Operator` = `RequireRole("platform-operator")`) + fatia vertical de **tenants** (página `/tenants` lista via `Secco.SecureGate.Client` on-behalf-of). **SecureGate ganhou** (ADR-0023): tenant de plataforma + role `platform-operator` (seed de referência) e **filtro do scope `securegate:admin` no `/connect/authorize`** — só operadores o recebem (login comum não escala para admin, defesa em profundidade ADR-0020); seed de DEV com client confidencial `secco-adminportal` + operador `operador@secco.local`. Testes: token provider, encaminhamento on-behalf-of, smoke de composição; E2E do SecureGate prova operador↔usuário-comum no scope admin
- [x] 7.2 Administração de identidade: página de detalhe do tenant (drill-in `/tenants/{id}`) com seções **Usuários** (listar + criar: e-mail/senha inicial/roles) e **Roles & permissões** (listar, criar, editar permissões em texto livre — PUT idempotente). Tudo on-behalf-of o operador via `Secco.SecureGate.Client`, com um `ISecureGateClientFactory` central que anexa o token; erros do client (400/409) traduzidos para mensagens amigáveis (ProblemDetails → detail, ADR-0020). Reusa os endpoints existentes do SecureGate (gated por `securegate:admin` — sem o problema cross-tenant da 7.3, que é por permissão). Testes: fábrica (token forwarding), serviços de usuário/role (client mockado), projeções
- [ ] 7.3 Visualização de logs por tenant (LogStream) — resolve a questão em aberto da ADR-0023 (autorização de leitura cross-tenant do operador)
- [ ] 7.4 Gestão de bancos de tenant (connection strings, write-only)

## Backlog (só após Fase 7 estável)
NotificationHub · Configuration · FeatureFlags · Audit

---

*Regra de ouro: NotificationHub, Configuration, FeatureFlags e Audit ficam no backlog até o quarteto SharedKernel + SDK + LogStream + SecureGate provar o padrão. Paralelizar sete produtos impede que qualquer um amadureça.*
