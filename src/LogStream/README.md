# Secco.LogStream

Produto de logging e observabilidade da Secco Platform: recebe, armazena e consulta logs gerais, logs de processos (com auditoria por status agregado) e logs de chamadas de API de qualquer aplicação da plataforma ou externa.

> **Em reescrita (Fase 4 do [roadmap](../../docs/roadmap.md)):** o LogStream é a reescrita do zero do RS.Logging sobre o SharedKernel + SDK, com multi-tenancy real database-per-tenant. Log geral disponível (4.3); log de processos, ApiCallLog e retenção chegam nas fases 4.4–4.6.

## Endpoints (v1)

| Endpoint | Descrição |
|---|---|
| `POST /api/v1/log-entries` | Registra um log — **ingestão assíncrona**: responde `202` com o Id definitivo (Guid v7); fila cheia responde `503` + `Retry-After` |
| `POST /api/v1/log-entries/batch` | Lote (até 500 itens por default); validação tudo-ou-nada |
| `GET /api/v1/log-entries/{id}` | Busca pontual no banco do tenant |
| `GET /api/v1/log-entries?from=&to=&level=&message=&correlationId=&page=&size=` | Busca paginada, mais recentes primeiro |
| `POST /api/v1/log-processes` | Cria um processo (`202` com o Id — já serve para enviar details) |
| `GET /api/v1/log-processes/{id}` | Processo com **status agregado** (pior nível dos details) e contagem |
| `GET /api/v1/log-processes?status=&name=&from=&to=&correlationId=&page=&size=` | A listagem **é** a auditoria — status sempre presente e filtrável |
| `POST /api/v1/log-processes/{id}/details` (+`/batch`) | Details do processo (ingestão assíncrona; fila FIFO única preserva a ordem pai→details) |
| `GET /api/v1/log-processes/{id}/details?page=&size=` | Details paginados, mais recentes primeiro |

Limites de ingestão configuráveis na seção `LogStream:Ingestion` (defaults: mensagem 16 KB, stack trace 128 KB, batch 500, fila 10.000 — ADR-0020).

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

## Rodando em desenvolvimento

Pré-requisitos: .NET 10 SDK e um SQL Server acessível (ex.: container):

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Secco@Dev123" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

```bash
dotnet run --project src/LogStream/Secco.LogStream.Api
```

Em **Development** o startup aplica as migrations em todos os bancos de tenant do catálogo (`appsettings.Development.json`, seção `Secco:Tenancy:Tenants`) e executa o seeding (ADR-0019). Fora de Development, nada é automático — migrations via processo controlado.

- OpenAPI: `GET /openapi/v1.json` (anônimo — o contrato é público por design)
- Scalar UI: `/scalar/v1` (apenas DEV)
- Health: `GET /health/live` e `GET /health/ready` (anônimos)

## Autenticação (ADR-0007)

Todos os endpoints de negócio exigem JWT (`Authorization: Bearer <token>`) — a `FallbackPolicy` da plataforma protege por default qualquer endpoint sem metadata explícita. Enquanto o SecureGate (Fase 6) não existe, DEV/Staging usam chave simétrica HS256 (`Secco:Authentication:DevelopmentSigningKey`) — **proibida em Production** (a API não sobe).

Gerando um token de teste em PowerShell (claims curtas: `sub`, `role`, `tenant_id`):

```powershell
$secret  = "secco-logstream-dev-key-minimo-32-chars!"   # DevelopmentSigningKey do ambiente
$tenant  = "018f0000-0000-7000-8000-000000000001"        # tenant do catálogo DEV
$b64 = { param($s) [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)) -replace '=+$' -replace '\+','-' -replace '/','_' }
$header  = & $b64 '{"alg":"HS256","typ":"JWT"}'
$exp     = [DateTimeOffset]::UtcNow.AddHours(1).ToUnixTimeSeconds()
$payload = & $b64 "{`"iss`":`"secco-dev`",`"aud`":`"secco-logstream`",`"sub`":`"dev-user`",`"role`":`"Admin`",`"tenant_id`":`"$tenant`",`"exp`":$exp}"
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

## Referência funcional

O RS.Logging (v2.0.0) permanece como referência do comportamento a atingir até a paridade — os desvios intencionais (tenancy real, ingestão 100% assíncrona com Guid v7, limites de ingestão, MariaDB fora) estão registrados no roadmap da Fase 4.
