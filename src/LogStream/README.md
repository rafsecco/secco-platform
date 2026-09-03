# Secco.LogStream

Produto de logging e observabilidade da Secco Platform: recebe, armazena e consulta logs gerais, logs de processos (com auditoria por status agregado), logs de chamadas de API e a trilha de auditoria de ação de usuário — de qualquer aplicação da plataforma ou externa.

> **Produto completo (Fase 4 do [roadmap](../../docs/roadmap.md) concluída, 4.1–4.7):** o LogStream é a reescrita do zero do RS.Logging sobre o SharedKernel + SDK, com multi-tenancy real database-per-tenant. Log geral, log de processos + auditoria, ApiCallLog, retenção opt-in e PostgreSQL como segundo provider — tudo entregue. **Incremento pós-Fase 4:** o `LogEntry` ganhou `ServiceName`/`Category` (o sink do `Secco.SDK.Logging` os preenche) e correlação por item no `/batch`; a trilha de auditoria (`AuditEntry`) chegou como recurso próprio, com ingestão **síncrona** e retenção independente do diagnóstico.

## Endpoints (v1)

| Endpoint | Descrição |
|---|---|
| `POST /api/v1/log-entries` | Registra um log — **ingestão assíncrona**: responde `202` com o Id definitivo (Guid v7); fila cheia responde `503` + `Retry-After` |
| `POST /api/v1/log-entries/batch` | Lote (até 500 itens por default); validação tudo-ou-nada; `correlationId` é **por item** — o valor do payload vence quando presente, o header `X-Correlation-Id` é o fallback |
| `GET /api/v1/log-entries/{id}` | Busca pontual no banco do tenant |
| `GET /api/v1/log-entries?from=&to=&level=&message=&correlationId=&serviceName=&category=&page=&size=` | Busca paginada, mais recentes primeiro; `serviceName`/`category` são igualdade exata (nunca `LIKE`) |
| `POST /api/v1/log-processes` | Cria um processo (`202` com o Id — já serve para enviar details) |
| `GET /api/v1/log-processes/{id}` | Processo com **status agregado** (pior nível dos details) e contagem |
| `GET /api/v1/log-processes?status=&name=&from=&to=&correlationId=&page=&size=` | A listagem **é** a auditoria de processo — status sempre presente e filtrável |
| `POST /api/v1/log-processes/{id}/details` (+`/batch`) | Details do processo (ingestão assíncrona; fila FIFO única preserva a ordem pai→details) |
| `GET /api/v1/log-processes/{id}/details?page=&size=` | Details paginados, mais recentes primeiro |
| `POST /api/v1/api-call-logs` | Registra chamada de API externa — headers sensíveis (`Authorization`, `Cookie`, `X-Api-Key`...) são **redigidos no servidor** (ADR-0020); bodies opcionais truncados em 64 KB |
| `GET /api/v1/api-call-logs/{id}` | Busca pontual |
| `GET /api/v1/api-call-logs?isSuccess=&method=&url=&statusCode=&from=&to=&correlationId=&page=&size=` | Busca paginada (diagnóstico de integrações) |
| `POST /api/v1/audit-entries` | Registra uma entrada de auditoria — **ingestão SÍNCRONA** (diferença deliberada, ver abaixo): responde `201` só depois do commit |
| `GET /api/v1/audit-entries/{id}` | Busca pontual |
| `GET /api/v1/audit-entries?from=&to=&actorId=&action=&resourceType=&resourceId=&correlationId=&page=&size=` | Busca paginada, mais recentes primeiro; todos os filtros são igualdade exata |

Limites de ingestão configuráveis na seção `LogStream:Ingestion` (defaults: mensagem 16 KB, stack trace 128 KB, batch 500, fila 10.000, nome de serviço 256, categoria 512, metadata de auditoria 16 KB — ADR-0020).

### Trilha de auditoria — por que é síncrona

Os outros três recursos respondem `202` (fila + worker; perder um log num pico é aceitável). A auditoria existe por obrigação legal — um registro que pode sumir numa fila cheia sem ninguém saber é o oposto do que se espera de uma trilha. `POST /api/v1/audit-entries` grava e só então responde `201`: ou o fato está persistido, ou o chamador recebe erro explícito. Consequência assumida: indisponibilidade do LogStream **bloqueia** quem audita, ao contrário de quem só loga. Sem batch, sem `PUT`, sem `DELETE` — uma trilha que se pode editar não é trilha. `ActorId`/`Action` são invariantes de domínio; `Metadata`, quando presente, precisa ser JSON válido e caber no limite configurado.

## Retenção (opt-in explícito, por classe de dado)

Sem configuração, **nada é expurgado** — apagar dados jamais é efeito colateral de default; configuração inválida também desativa o worker (fail-safe). Diagnóstico (log geral, processos, chamadas de API) e auditoria têm **janelas independentes** — a de diagnóstico nunca leva a trilha de auditoria junto, e a janela de auditoria é `null` por padrão (a trilha nunca expira sem configuração explícita):

```json
"LogStream": { "Retention": {
    "DefaultDays": 30,
    "IntervalHours": 6,
    "DaysByTenant": { "<guid-do-tenant>": 90 },
    "AuditDefaultDays": null,
    "AuditDaysByTenant": { "<guid-do-tenant>": 2555 }
} }
```

O worker (`ADR-0015` camada 1) itera os bancos de tenant via catálogo a cada ciclo; details de processos são removidos pelo cascade da FK. Falha em um tenant é logada e não interrompe os demais.

O corte da auditoria é pelo `dt_created_at` (carimbo do servidor), **não** pelo `dt_occurred_at` declarado pelo chamador: retenção é operação destrutiva e não pode ser governada por input externo — um `occurredAt` forjado no passado apagaria a trilha antes da hora (ADR-0020).

## Arquitetura

Quatro camadas com dependências apontando para dentro (ADR-0002):

| Projeto | Papel |
|---|---|
| `Secco.LogStream.Api` | Endpoints, composição (`AddSeccoPlatform()`), OpenAPI + Scalar |
| `Secco.LogStream.Application` | Casos de uso; retorna `Result<T>` (ADR-0004) |
| `Secco.LogStream.Domain` | Entidades e regras de negócio; só referencia o SharedKernel |
| `Secco.LogStream.Infrastructure` | EF Core (SQL Server padrão, ADR-0018), migrations, repositórios |
| `Secco.LogStream.Client` | Client NSwag gerado (nasce na fase 4.3, ADR-0006) |

**Multi-tenancy (ADR-0005):** cada tenant possui banco próprio. Não existe coluna `TenantId` nem filtro por tenant — o isolamento é físico. A connection string vem do catálogo via `ITenantConnectionFactory` a cada requisição; o tenant é resolvido pela claim `tenant_id` do token (primário) ou header `X-Tenant-Id` (cenários internos, sem claim).

**Providers (ADR-0018):** SQL Server (padrão) e PostgreSQL, com migrations em assemblies separados por engine (`Secco.LogStream.Migrations.SqlServer`/`.Postgres`) e seleção por configuração — todos os tenants de um deployment usam o mesmo engine:

```json
"LogStream": { "Database": { "Provider": "PostgreSql" } }
```

Nova migration (uma por engine, no mesmo PR): `dotnet ef migrations add <Nome> --project src/LogStream/Secco.LogStream.Migrations.<Engine>`. Full-text search ficou no backlog — a busca da v1 é por substring (`LIKE`).

## Rodando em desenvolvimento

Pré-requisitos: .NET 10 SDK e a infraestrutura local (SQL Server + MailHog), que vem do
`docker-compose.yml` da **raiz** do monorepo:

```bash
docker compose up -d                      # só a infra, sem nenhuma API
dotnet run --project src/LogStream/Secco.LogStream.Api   # https://localhost:4002
```

No VS Code isso são as duas primeiras entradas do F5 — ver [`.vscode/launch.json`](../../.vscode/launch.json).

Ou tudo em container (SQL Server + API, com migrations/seed automáticos):

```bash
docker compose --profile logstream up -d --build   # API em http://localhost:4102
```

Em **Development** o startup aplica as migrations em todos os bancos de tenant do catálogo (`appsettings.Development.json`, seção `Secco:Tenancy:Tenants`) e executa o seeding (ADR-0019). Fora de Development, nada é automático — migrations via processo controlado.

- OpenAPI: `GET /openapi/v1.json` (anônimo — o contrato é público por design)
- Scalar UI: `/scalar/v1` (apenas DEV)
- Health: `GET /health/live` e `GET /health/ready` (anônimos)

## Autenticação e autorização (ADR-0007/0021)

Todos os endpoints de negócio exigem JWT (`Authorization: Bearer <token>`) — a `FallbackPolicy` da plataforma protege por default qualquer endpoint sem metadata explícita. Em produção a Authority é o SecureGate (JWKS); DEV/Staging podem usar chave simétrica HS256 (`Secco:Authentication:DevelopmentSigningKey`) — **proibida em Production** (a API não sobe).

Desde a Fase 6.4, os endpoints também exigem **permissão** (ADR-0021): ingestão pede `log-entries:write` / `log-processes:write` / `api-call-logs:write` e consulta pede as `*:read` correspondentes, resolvidas do `role` do token em runtime (cache TTL curto, fail-closed). Em DEV o mapeamento vem de `Secco:Authorization:Roles` (o role `dev-admin` já vem com todas); em produção, do SecureGate via `AddSecureGatePermissionResolver()`.

Gerando um token de teste em PowerShell (claims curtas: `sub`, `role`, `tenant_id` — o role precisa ter as permissões):

```powershell
$secret  = "secco-logstream-dev-key-minimo-32-chars!"   # DevelopmentSigningKey do ambiente
$tenant  = "018f0000-0000-7000-8000-000000000001"        # tenant do catálogo DEV
$b64 = { param($s) [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)) -replace '=+$' -replace '\+','-' -replace '/','_' }
$header  = & $b64 '{"alg":"HS256","typ":"JWT"}'
$exp     = [DateTimeOffset]::UtcNow.AddHours(1).ToUnixTimeSeconds()
$payload = & $b64 "{`"iss`":`"secco-dev`",`"aud`":`"secco-logstream`",`"sub`":`"dev-user`",`"role`":`"dev-admin`",`"tenant_id`":`"$tenant`",`"exp`":$exp}"
$hmac    = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
$sig     = [Convert]::ToBase64String($hmac.ComputeHash([Text.Encoding]::ASCII.GetBytes("$header.$payload"))) -replace '=+$' -replace '\+','-' -replace '/','_'
"$header.$payload.$sig"
```

## Contrato e testes

- O [`openapi/openapi.json`](Secco.LogStream.Api/openapi/openapi.json) versionado **é** o contrato (ADR-0006): um teste de integração compara o documento gerado pela API com o snapshot e falha o CI em qualquer divergência. Mudança intencional: rodar os testes com `SECCO_UPDATE_OPENAPI=true` e commitar o diff junto do client regenerado, no mesmo PR.
- Testes de integração usam **SQL Server real via Testcontainers** (ADR-0012), provando inclusive o isolamento físico entre bancos de tenant:

```bash
dotnet test tests/LogStream/Secco.LogStream.Tests/Secco.LogStream.Tests.csproj
```
