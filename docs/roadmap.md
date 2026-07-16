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
- [x] 7.3 Visualização de logs por tenant (LogStream) — **resolve a questão em aberto da ADR-0023 via ADR-0024**: o token do operador é **tenant-less** (não carrega `tenant_id`; escolhe o tenant por requisição via `X-Tenant-Id` — o caminho "sem claim → header" que a ADR-0005 já permite, sem reformar a regra de conflito), e a autorização concede ao papel `platform-operator` um **read-set fixo** (`log-entries:read`/`log-processes:read`/`api-call-logs:read`) em qualquer tenant via **caso especial na resolução do SecureGate** (produtos/SDK inalterados). AdminPortal: página `/tenants/{id}/logs` (drill-in) com busca paginada de log-entries (nível/mensagem) via `Secco.LogStream.Client` on-behalf-of, anexando token + `X-Tenant-Id`. Corrigido de passagem um mismatch de contrato do LogStream (enum sem `type: string` no OpenAPI → client desserializava enum como número; partial class no client adiciona `JsonStringEnumConverter`). Testes: resolução do read-set cross-tenant, token do operador sem `tenant_id`, `LogQueryService` (token+header+projeção)
- [x] 7.4 Gestão de bancos de tenant: seção **Bancos** na página do tenant (drill-in) — lista os produtos que têm banco (a lista `Products` do tenant; connection strings **nunca** aparecem, write-only ADR-0020) e cadastra/rotaciona via o PUT idempotente existente (produto em texto livre + connection string em input password). Reusa o endpoint da 6.3, sem novos endpoints no SecureGate. Teste: `UpsertDatabaseAsync` encaminha a connection string ao client

**Fase 7 concluída** — o AdminPortal cobre gestão de tenants, administração de identidade (usuários/roles/permissões), visualização de logs cross-tenant e gestão de bancos de tenant. A fundação da plataforma (SharedKernel + SDK + LogStream + SecureGate + AdminPortal) está completa; o NotificationHub inicia na Fase 8, os demais produtos (Configuration, FeatureFlags, Audit) seguem no backlog.

## Fase 8 — Secco.NotificationHub (em andamento)

> Decisão (2026-07-16): v1 nasce enxuto e desacoplado — só canal e-mail (abstração
> de canal interna pronta para um 2º canal futuro, sem prever a forma); sem motor
> de templates (o chamador manda assunto/corpo prontos); sem acoplamento ao
> SecureGate (o chamador resolve/informa o contato — e-mail — diretamente).
> Broker de mensageria (ADR-0015 Camada 3, deixada em aberto para este produto)
> continua adiado — v1 usa o mesmo padrão nativo/Hangfire já provado no
> LogStream/SecureGate; a Camada 3 só abre quando um caso real de
> multi-produtor/multi-consumidor justificar.

- [ ] 8.1 Fundação: 4 camadas (ADR-0002) + `AddSeccoPlatform()` + `AddSeccoOpenApi()` + `openapi.json` versionado com teste de contrato + `NotificationHubDbContext` por tenant (`SeccoNamingConvention`, ADR-0017) + migrations SQL Server + Testcontainers — gerado a partir do `dotnet new secco-service`
- [ ] 8.2 Envio de e-mail: `POST /api/v1/notifications` recebe destinatário (e-mail já resolvido pelo chamador) + assunto/corpo prontos; enfileira via `IBackgroundJobScheduler` (Hangfire/SQL Server, ADR-0015) com retry automático em falha transitória do provider (SMTP/SendGrid); `GET /api/v1/notifications/{id}` devolve status (`Pending`/`Sent`/`Failed` + motivo da falha); limites de tamanho de payload e taxa de ingestão (ADR-0020); `Secco.NotificationHub.Client` nasce com o 1º endpoint real (ADR-0006)
- [ ] 8.3 Paridade: segundo provider de banco (PostgreSQL, ADR-0018), Dockerfile/compose de desenvolvimento

## Backlog (só após Fase 8 estável)

Descrições de trabalho — nenhuma ADR ainda define escopo real para estes produtos; detalhar via rounds de design (como o NotificationHub) só quando a vez de cada um chegar.

- **Configuration** — configuração dinâmica por tenant (valores operacionais, não binários como feature flags) sem precisar de redeploy; um catálogo central de settings por tenant/produto, análogo em espírito ao catálogo de tenants do SecureGate.
- **FeatureFlags** — ativação/desativação de funcionalidades em runtime, por tenant (ou por %, por role); controla rollout gradual e kill-switch de feature sem deploy.
- **Audit** — trilha de auditoria centralizada e pesquisável de ações de negócio entre produtos ("quem fez o quê, quando, em qual tenant"); complementar ao `AuditableEntity` do SharedKernel, que só grava `CreatedBy`/`UpdatedBy` local em cada entidade.

---

*Regra de ouro: Configuration, FeatureFlags e Audit ficam no backlog até o NotificationHub provar o padrão de mais um produto adotando a fundação. Paralelizar produtos impede que qualquer um amadureça.*
