# Secco.SDK.Logging + trilha de auditoria — design

**Data:** 2026-09-03
**Status:** aprovado para planejamento
**Itens de origem:** as issues [#1](https://github.com/rafsecco/secco-platform/issues/1)
(`adopter-demand`, `blocker`) — o provider prometido pela ADR-0008 nunca foi implementado, o que
trava o item "Integração com `Secco.LogStream.Client` (logs)" da Fase 0 do roadmap do
`secco-intranet` — e [#2](https://github.com/rafsecco/secco-platform/issues/2), a trilha de
auditoria de ação de usuário.

**Por que juntas.** A #1, sozinha, resolveria o enriquecimento de "nome do serviço" prometido pela
ADR-0008 com um prefixo na mensagem, porque o `LogEntry` não tem campo para isso. Decisão desta
rodada (2026-09-03): o campo entra de verdade. A partir daí a entrega já carrega migration nos dois
engines, regeneração de contrato e de client — que é exatamente o custo que a #2 também paga. Fazer
as duas numa passada só evita duas migrations e duas rodadas de `SECCO_UPDATE_OPENAPI`.

## Contexto

A ADR-0008 decidiu, em 2026-07-04:

> Produtos usam `ILogger<T>` normalmente; um provider do SDK (`AddLogStream()`) envia os logs ao
> Secco.LogStream via `Secco.LogStream.Client` (batch + fila local + retry — nunca bloqueia o
> request). Correlation id, tenant id e nome do serviço são enriquecidos automaticamente pelo SDK.

O que existe hoje é o `Secco.LogStream.Client` com `AddLogStreamClient()` — o client HTTP gerado.
Nenhum `ILoggerProvider`. O produto de log está completo (ingestão assíncrona, retenção, dois
providers de banco, leitura cross-tenant do operador) e **não há caminho para um produto escrever
nele sem código manual**.

Nota de nomenclatura: a ADR-0006 usa `AddLogStream(o => o.BaseUrl = ...)` num exemplo que era sobre
registrar o *client*; a implementação chamou aquilo de `AddLogStreamClient()`. Esta entrega ocupa o
nome `AddLogStream()` com o significado que a ADR-0008 lhe deu — **o sink**. Os dois convivem: o
sink registra o client internamente.

### Três achados que mudam o desenho

**1. O batch atual não serve a um sink.** `POST /api/v1/log-entries/batch` deriva o `correlationId`
do header `X-Correlation-Id` da requisição e o aplica a todos os itens do lote
(`LogEntryEndpoints.ToCommand`). Uma fila local acumula logs de requisições diferentes; enviá-los
em lote hoje significa carimbar todos com a mesma correlação — perder exatamente o enriquecimento
que a ADR-0008 promete. O payload precisa carregar a correlação por item.

**2. Os contextos de enriquecimento são `Scoped`; um `ILoggerProvider` é singleton.**
`CorrelationContext` e `TenantContext` só existem dentro de um escopo de DI, populados pelos
middlewares. Um logger singleton não os alcança. Dentro de job do Hangfire (`TenantJobRunner`) há
escopo mas não há `HttpContext`, então `IHttpContextAccessor` também não resolve o caso geral.

**3. O LogStream é database-per-tenant e exige permissão.** Ingestão pede `log-entries:write`
(ADR-0021) e o destino do log é o banco do tenant. O sink precisa de um token client credentials
(scope `logstream`, audience `secco-logstream`) **e** de `X-Tenant-Id` por lote — o token de máquina
é tenant-less, então vale o caminho "sem claim → header" da ADR-0005, sem tocar na regra de conflito.

## Decisões tomadas nesta rodada

1. **Pacote novo `Secco.SDK.Logging`** (publicável, `MinVerTagPrefix` `sdk-logging/v`), referenciando
   `Secco.SDK.AspNetCore` + `Secco.LogStream.Client`. Descartado colocar no `Secco.SDK.AspNetCore`:
   inverteria a direção "SDK não conhece produto" e faria todo consumidor do SDK — inclusive o
   próprio `Secco.LogStream.Api` — arrastar o client do LogStream. Descartado colocar no
   `Secco.LogStream.Client`: o enriquecimento precisa de `ICorrelationContext`/`ITenantContext`, e o
   client passaria a depender do SDK; além disso a ADR-0008 diz "provider do SDK".
2. **O handler de client credentials sobe para o SDK.** `SecureGateClientCredentialsHandler` sai de
   `Secco.SecureGate.Client/Catalog/` e vira `Secco.SDK.AspNetCore/Authentication/SeccoClientCredentialsHandler`
   (+ `SeccoAccessTokenStore`). Justificativa: o handler já é OAuth 2 puro contra `/connect/token` —
   o próprio comentário dele registra a exceção consciente à ADR-0006 por ser protocolo, não
   contrato de produto. O `Secco.SecureGate.Client` passa a consumi-lo (já referencia o SDK). Assim
   o `Secco.SDK.Logging` não depende do pacote de identidade só para pegar um token.
3. **Tenant de plataforma opcional para logs sem tenant.** `Secco:LogStream:PlatformTenantId`: se
   configurado, log emitido fora de um escopo com tenant (startup, worker, job sem tenant) vai para
   ele; se não, é descartado e contado. Descartado torná-lo obrigatório — forçaria todo adotante a
   provisionar um tenant de plataforma só para ligar o log.
4. **Mudança aditiva no contrato do LogStream.** `CreateLogEntryRequest` ganha `CorrelationId?`; o
   servidor usa o do payload quando vier e o do header caso contrário. Aditiva, então sem `/v2`
   (ADR-0009). `openapi.json` + client regenerados no mesmo PR (ADR-0006). Descartado agrupar a fila
   por `(tenant, correlação)` sem tocar no contrato: em pico daria quase um POST por requisição da
   aplicação, o que derrota o propósito do batch.
5. **Contexto ambiente no SDK.** Um `SeccoAmbientContext` (`AsyncLocal`) interno ao
   `Secco.SDK.AspNetCore`, escrito pelos middlewares de correlation/tenancy e por
   `TenantScopeExtensions.SetTenant`, exposto por uma superfície mínima de leitura. Os contextos
   `Scoped` continuam sendo a API pública para o código de aplicação; o ambiente existe para quem
   é singleton por natureza — o logger.
6. **Sem ADR nova.** A ADR-0008 já decide o quê; esta entrega é a implementação dela. O que fica
   registrado é o log de decisões de design (as escolhas desta rodada e as alternativas descartadas).
7. **A trilha de auditoria é recurso novo dentro do LogStream**, não produto separado nem coluna no
   `LogEntry`. Reusa os dois providers de banco, o client publicado e a leitura cross-tenant do
   operador (ADR-0024). Descartado `Secco.Audit` como produto: quatro camadas, banco, migrations nos
   dois engines, client, CI, Docker e mais um serviço para o adotante subir — tudo para uma entidade.
   Descartado campo no `LogEntry`: misturaria dado com prazo legal (anos) e dado de diagnóstico
   (dias) na mesma tabela e na mesma retenção, que é a objeção central da issue #2.
8. **Retenção deixa de ser única.** Diagnóstico e auditoria passam a ter janelas independentes, e a
   janela de auditoria é `null` por padrão — auditoria não expira a menos que alguém configure um
   prazo. Isso preserva a postura fail-safe que já existe (sem configuração, nada é apagado) e
   resolve a restrição que a issue nomeia.
9. **Ingestão de auditoria é síncrona**, ao contrário dos outros três recursos. Ver a justificativa
   na seção própria: uma fila com descarte controlado é a decisão certa para log de diagnóstico e a
   errada para um registro com obrigação legal.
10. **Sem IP e user agent no v1.** São dado pessoal com peso de LGPD e ainda não têm consumidor
    declarado. Quem precisar põe em `Metadata` conscientemente.
11. **O ator é declarado pelo produto chamador**, não derivado do token. Não há alternativa: o
    chamador se autentica por client credentials, então o `sub` do token é a máquina, não a pessoa
    que agiu. O nível de confiança é o mesmo do resto do payload de log e está registrado na seção
    de segurança.

## Arquitetura

### Superfície pública

```csharp
namespace Secco.SDK.Logging;

/// <summary>Opções do sink (seção Secco:LogStream).</summary>
public sealed class LogStreamLoggerOptions
{
    public const string SectionKey = "Secco:LogStream";

    public bool Enabled { get; set; } = true;
    public string? BaseUrl { get; set; }              // URL da API do LogStream
    public string? ClientId { get; set; }             // client credentials
    public string? ClientSecret { get; set; }         // nunca logado
    public string Scope { get; set; } = "logstream";  // audience secco-logstream
    public string? ServiceName { get; set; }          // default: nome do assembly de entrada
    public Guid? PlatformTenantId { get; set; }       // destino dos logs sem tenant; null = descarta

    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
    public int QueueCapacity { get; set; } = 10_000;
    public int BatchSize { get; set; } = 100;
    public int FlushIntervalMs { get; set; } = 2_000;
    public int ShutdownFlushTimeoutMs { get; set; } = 5_000;
    public int MaxMessageLength { get; set; } = 8_000;
    public int MaxStackTraceLength { get; set; } = 16_000;
}

public static class SeccoLogStreamLoggingExtensions
{
    /// <summary>Registra o sink lendo a seção Secco:LogStream do IConfiguration do host.</summary>
    public static IServiceCollection AddLogStream(this IServiceCollection services);

    /// <summary>Registra o sink com configuração em código (sobrepõe a seção).</summary>
    public static IServiceCollection AddLogStream(this IServiceCollection services,
                                                  Action<LogStreamLoggerOptions> configure);
}
```

Binding por `AddOptions<LogStreamLoggerOptions>().BindConfiguration(...).Validate(...).ValidateOnStart()`,
seguindo o que a rodada do `Secco.SDK.Testing` fixou (o `BindSection` foi apagado de propósito).

### Componentes

| Tipo | Vida | Responsabilidade |
| --- | --- | --- |
| `LogStreamLoggerProvider` | singleton | `ILoggerProvider`; cria um `LogStreamLogger` por categoria e decide, uma vez por categoria, se ela é ignorada |
| `LogStreamLogger` | por categoria | formata via `formatter(state, exception)`, captura tenant/correlação/serviço do ambiente, trunca e faz `TryWrite`. Nunca bloqueia, nunca lança |
| `LogStreamLogQueue` | singleton | `Channel<PendingLogEntry>` bounded (`FullMode = DropWrite`) + contador de descartes |
| `LogStreamDispatcher` | `BackgroundService` | drena por `BatchSize` ou `FlushIntervalMs`, agrupa por tenant, um `POST /batch` por tenant com `X-Tenant-Id`, flush com timeout no `StopAsync` |

`PendingLogEntry` é um record interno: `(Guid TenantId, Guid? CorrelationId, LogEntryLevel Level,
string Message, string? StackTrace, DateTimeOffset Timestamp)`. O tenant é resolvido **no momento do
log** (o worker roda fora do escopo), já com o fallback de plataforma aplicado — entradas sem tenant
nem fallback nem chegam à fila.

### Fluxo

```
ILogger<T>.LogError(...)
  → LogStreamLogger: categoria permitida? nível >= MinimumLevel?
  → captura ambiente (tenant, correlação) + ServiceName
  → tenant nulo? PlatformTenantId ?? descarta (contador)
  → trunca Message/StackTrace
  → LogStreamLogQueue.TryWrite            [não bloqueia; fila cheia → descarta + contador]
        ↓
  LogStreamDispatcher (BackgroundService)
  → lê até BatchSize ou até FlushIntervalMs
  → GroupBy(TenantId)
  → por grupo: ILogStreamClient.LogEntriesBatch(X-Tenant-Id, itens com CorrelationId por item)
  → falha: resiliência do SDK tenta; esgotou → descarta o lote e conta. Nunca relança.
```

`ServiceName` e a categoria do `ILogger` vão em **campos próprios** do `LogEntry`
(`ds_service_name`, `ds_category`) — ver a seção de mudanças de schema. A alternativa de prefixar a
mensagem foi descartada: repetiria, para serviço e categoria, exatamente a crítica que a issue #2
faz ao ator de hoje — dado que só existe dentro do texto não é filtrável nem pesquisável.

### Anti-recursão — requisito de correção

O dispatcher usa `HttpClient`, que loga em `System.Net.Http.HttpClient.*`. Sem filtro, cada envio de
lote gera logs que entram na fila, que geram outro envio: laço que enche a fila sozinho e nunca
converge. O provider **ignora** por prefixo de categoria:

- `System.Net.Http.*`
- `Microsoft.Extensions.Http.*`
- `Polly*`
- `Secco.SDK.Logging.*`

O próprio dispatcher reporta descartes e falhas por um `ILogger` cuja categoria cai nessa lista —
ou seja, vai para os providers locais (console, arquivo) e nunca para o LogStream.

### Segurança (ADR-0020)

| Vetor | Tratamento |
| --- | --- |
| Input não confiável | Mensagem e stack trace são texto arbitrário de terceiros; truncados em `MaxMessageLength`/`MaxStackTraceLength` **antes** de entrar na fila |
| Negação de serviço | Fila bounded com descarte contado; um pico de log não cresce memória nem derruba o processo. Falha do LogStream nunca propaga para o request (ADR-0008) |
| Vazamento | `ClientSecret` nunca é logado nem entra em mensagem de erro; corpo de resposta de falha do LogStream não entra na mensagem, só o status |
| Isolamento de tenant | O tenant é capturado no momento do log e o lote é agrupado por tenant — nenhum item viaja num lote de outro tenant. Fallback de plataforma é explícito e opcional |
| Autenticação | Client credentials com o scope mínimo (`logstream`); o token não carrega `tenant_id`, o tenant vai por header |
| Dependência nova | Nenhuma: `System.Threading.Channels` é BCL |

Nota operacional para o README: o client OIDC do produto precisa de um role com `log-entries:write`
**em cada tenant** para o qual ele loga (a resolução é por `(tenant_id, role)`, ADR-0021). Sem isso o
LogStream responde 403 e o lote é descartado — visível pelo contador, não por exceção no produto.

## Trilha de auditoria — o recurso `AuditEntry` (issue #2)

### Por que é entidade própria e não coluna no `LogEntry`

A issue #2 dá o argumento e ele se sustenta: auditoria e log de diagnóstico têm ciclos de vida
diferentes. Auditoria vive anos por obrigação legal; log de diagnóstico expira em dias. Uma trilha de
auditoria sob a retenção do log é uma trilha que some. Somem também os campos: um `LogEntry` tem
severidade e stack trace, que não significam nada num registro de "fulano baixou o documento X";
um `AuditEntry` tem ator, ação e recurso, que não significam nada num log de exceção. É a mesma
separação que o NotificationHub já fez entre `Notification` (e-mail, com status de entrega) e
`InAppNotification` (lido/não lido) — entidades separadas porque o ciclo de vida é diferente.

### Ingestão síncrona — a diferença deliberada

Os outros três recursos do LogStream respondem `202 Accepted`: o Id é gerado antes do INSERT e a
persistência acontece num worker, atrás de uma fila com descarte controlado. Para diagnóstico isso
é correto — perder um log num pico é melhor que derrubar o produto que o emitiu.

Para auditoria é errado. Um registro que existe por obrigação legal não pode desaparecer numa fila
cheia sem que ninguém saiba. `POST /api/v1/audit-entries` responde **`201 Created`** depois do
commit: ou o fato está gravado, ou o chamador recebe erro e decide o que fazer. É mais lento e é o
ponto — a diferença de custo é exatamente a garantia que se está comprando.

Consequência assumida: LogStream indisponível **bloqueia** quem audita, ao contrário de quem loga.
Um produto que não pode parar por isso tem a saída de gravar localmente e reenviar, mas isso é
decisão do produto, não default da plataforma.

### Entidade

```csharp
public sealed class AuditEntry : BaseEntity   // Id Guid v7 herdado
{
    public string ActorId { get; }            // ds_actor_id     — sub do usuário, ou client id
    public string? ActorName { get; }         // ds_actor_name   — snapshot legível no momento do fato
    public ActorType ActorType { get; }       // ie_actor_type   — User | Client
    public string Action { get; }             // ds_action       — verbo canônico: "documento.download"
    public string? ResourceType { get; }      // ds_resource_type
    public string? ResourceId { get; }        // ds_resource_id
    public string? Metadata { get; }          // ds_metadata     — JSON livre, com limite de tamanho
    public Guid? CorrelationId { get; }       // correlation_id
    public DateTimeOffset OccurredAt { get; } // dt_occurred_at  — quando o fato aconteceu (declarado)
    public DateTimeOffset CreatedAt { get; }  // dt_created_at   — quando o LogStream registrou
}
```

Imutável, como todo registro de log. Tenant não é atributo: o isolamento é físico, por banco
(ADR-0005). Nomes de coluna pela convention global da ADR-0017 — nunca digitados à mão.

`OccurredAt` e `CreatedAt` são separados de propósito. `OccurredAt` é declarado pelo chamador e é o
que interessa à auditoria; `CreatedAt` é o carimbo do servidor e é o que permite detectar
divergência (relógio errado, reenvio tardio, tentativa de backdating). Guardar só um dos dois perde
uma das duas perguntas.

**O expurgo corta pelo `CreatedAt`, nunca pelo `OccurredAt`** (ADR-0020). Retenção é operação
destrutiva, e `OccurredAt` é input externo: se ele governasse o corte, um valor forjado no passado
apagaria a própria trilha antes da hora. `OccurredAt` serve para consultar o fato, não para
expurgá-lo — e há teste de regressão para isso.

Invariantes de domínio: `ActorId` e `Action` obrigatórios; `Metadata`, quando presente, precisa ser
JSON válido e caber no limite configurado.

Índices: `(dt_occurred_at DESC)`, `(ds_actor_id, dt_occurred_at DESC)` e
`(ds_resource_type, ds_resource_id)` — os três eixos de consulta que a issue descreve ("quem baixou
qual documento, quem publicou, quem entrou na sessão").

### Endpoints e permissões

| Verbo | Rota | Permissão | Resposta |
| --- | --- | --- | --- |
| POST | `/api/v1/audit-entries` | `audit-entries:write` | `201 Created` |
| GET | `/api/v1/audit-entries` | `audit-entries:read` | `200` paginado, mais recentes primeiro |
| GET | `/api/v1/audit-entries/{id}` | `audit-entries:read` | `200` / `404` |

Sem endpoint de batch: batch existe para amortizar custo de ingestão de alto volume, que é
justamente o regime em que o descarte é aceitável — não é o caso aqui. Sem `DELETE` e sem `PUT`:
uma trilha que se pode editar não é trilha.

Filtros do GET: `from`, `to`, `actorId`, `action`, `resourceType`, `resourceId`, `correlationId`,
`page`, `size` — todos opcionais, todos com igualdade exata, nenhum `LIKE`.

`audit-entries:read` **entra no read-set fixo do `platform-operator`** (ADR-0024), junto com os
outros `*:read` de log. É a consequência de o operador já ler o log de qualquer tenant: um recurso
de log fora do read-set simplesmente não aparece no AdminPortal. Fica registrado como escolha, não
como detalhe — dá ao operador de plataforma visão da trilha de auditoria de todos os tenants.

### Retenção por classe de dado

`LogStreamRetentionOptions` ganha um segundo par de janelas:

```csharp
public int? DefaultDays { get; set; }                       // diagnóstico (como hoje)
public Dictionary<Guid, int> DaysByTenant { get; }          // override por tenant (como hoje)
public int? AuditDefaultDays { get; set; }                  // auditoria; NULO = nunca expira
public Dictionary<Guid, int> AuditDaysByTenant { get; }     // override por tenant
```

Descartado um dicionário genérico `DaysByResource`: há duas classes de dado com prazos
qualitativamente diferentes, não N recursos com prazos arbitrários. Dois pares nomeados dizem a
verdade do domínio e não quebram a configuração de quem já usa `DefaultDays`.

O `LogRetentionWorker` passa a resolver as duas janelas por tenant e a expurgar `tb_audit_entries`
apenas quando a janela de auditoria estiver configurada. A postura fail-safe existente vale
igualmente: configuração ausente ou inválida = nada é apagado.

### Mudanças no `LogEntry` (o campo de serviço da ADR-0008)

Duas colunas novas, ambas nuláveis — mudança aditiva, sem breaking change de contrato:

- `ds_service_name` — o produto que emitiu o log, preenchido automaticamente pelo sink.
- `ds_category` — a categoria do `ILogger` (ex.: `Secco.Intranet.Documentos.UploadHandler`).

Ambas entram como filtro opcional na busca de `log-entries`. Descartado incluir também
`ds_exception_type` nesta rodada: útil em triagem, mas sem consumidor pedindo agora.

## Escopo

**Dentro:**

- Pacote `Secco.SDK.Logging` completo (provider, logger, fila, dispatcher, options, `AddLogStream()`).
- `SeccoAmbientContext` no `Secco.SDK.AspNetCore`, alimentado pelos middlewares e por `SetTenant`.
- ~~Promoção do handler de client credentials para o SDK~~ — **feito** (commit `18abe4c`).
- ~~Esqueleto do pacote `Secco.SDK.Logging`~~ — **feito** (commit `18abe4c`).
- `CorrelationId?`, `ServiceName?` e `Category?` em `CreateLogEntryRequest` + handlers + filtros de
  busca + `openapi.json` + client regenerados.
- Recurso `AuditEntry` completo: domínio, mapeamento, repositório, handlers, endpoints, permissões,
  migrations nos **dois** engines (SQL Server e PostgreSQL).
- Retenção por classe de dado no `LogRetentionWorker` e nas options.
- `audit-entries:read` no read-set do `platform-operator` no SecureGate (ADR-0024).
- Testes (abaixo), READMEs, `docs/roadmap.md`, `docs/design-decisions-log.md`.

**Fora (registrado, não feito):**

- Escopos do `ILogger` (`BeginScope`) como dado estruturado — o `LogEntry` não tem onde guardar.
- OpenTelemetry (a outra metade da ADR-0008) — não é o que a issue #1 pede.
- IP e user agent no `AuditEntry` — LGPD sem consumidor declarado; cabe em `Metadata` até doer.
- Tela de auditoria no AdminPortal — a #2 pede o recurso; a tela é rodada própria.
- Assinatura/encadeamento à prova de adulteração dos registros de auditoria (hash chain). A trilha
  hoje é apenas append-only por ausência de endpoint de escrita; quem tem acesso ao banco altera.
  Se isso virar requisito, é ADR nova.

## Testes

Unit (sem infraestrutura), em `tests/SDK/Secco.SDK.Logging.Tests`:

- `Log_WhenCategoryIsHttpClient_IsIgnored` — a guarda anti-recursão.
- `Log_WhenBelowMinimumLevel_IsIgnored`.
- `Log_WhenQueueIsFull_DropsAndCounts` — e não lança, e não bloqueia.
- `Log_WhenNoTenantAndNoPlatformTenant_IsDropped`.
- `Log_WhenNoTenantAndPlatformTenantConfigured_UsesPlatformTenant`.
- `Log_WhenMessageExceedsLimit_IsTruncated`.
- `Dispatch_WhenBatchHasMultipleTenants_SendsOneCallPerTenant` (client via NSubstitute).
- `Dispatch_WhenClientThrows_DiscardsBatchAndDoesNotRethrow`.
- `Map_LogLevel_To_LogEntryLevel` — a compatibilidade numérica documentada no enum.

Integração, com `SeccoApiFactory` (ADR-0027), em `tests/LogStream/Secco.LogStream.Tests`:

- `Batch_WhenPayloadCarriesCorrelationId_PersistsPerItemCorrelation` — a mudança de contrato.
- `Batch_WhenPayloadOmitsCorrelationId_FallsBackToHeader` — a compatibilidade com o que existe.
- `Search_WhenFilteredByServiceName_ReturnsOnlyThatService` — as colunas novas viraram filtro.
- Teste de contrato `OpenApiContractTests` atualizado com `SECCO_UPDATE_OPENAPI=true`.

Auditoria — unit em `tests/LogStream/Secco.LogStream.Tests`:

- `Create_WhenActorIdMissing_ThrowsDomainInvariant` e o equivalente para `Action`.
- `Create_WhenMetadataIsNotValidJson_ReturnsFailure` (via `Result<T>`, ADR-0004).
- `Create_WhenMetadataExceedsLimit_ReturnsFailure`.
- `ResolveAuditDays_WhenNotConfigured_ReturnsNull` — a garantia de "auditoria não expira por padrão".
- `ResolveAuditDays_WhenTenantOverridden_PrefersOverride`.

Auditoria — integração com `SeccoApiFactory`:

- `Post_WhenValid_Returns201AndPersistsBeforeResponding` — a diferença síncrona é o ponto: o GET
  logo depois **tem** que encontrar o registro, sem espera.
- `Post_WithoutWritePermission_Returns403`.
- `Search_WhenFilteredByActor_ReturnsOnlyThatActor`.
- `Purge_WhenOnlyDiagnosticWindowConfigured_KeepsAuditEntries` — a regressão que mais dói se
  quebrar: a retenção de diagnóstico não pode levar a trilha junto.
- Paridade PostgreSQL do recurso novo, no molde do que o LogStream já faz para os outros três.

## Riscos

- **Promoção do handler mexe em código publicado.** `Secco.SecureGate.Client` 0.x tem o tipo como
  `internal`, então mover não quebra consumidor externo; o risco é de regressão silenciosa no
  catálogo e na resolução de permissões. Mitigação: os testes existentes do SecureGate cobrem os
  dois caminhos e devem passar sem alteração.
- **`AsyncLocal` mal posicionado vaza contexto entre requisições.** Escrito dentro do
  `InvokeAsync` do middleware, o valor flui para baixo e não sobe. Mitigação: teste de integração
  com duas requisições concorrentes de tenants diferentes.
