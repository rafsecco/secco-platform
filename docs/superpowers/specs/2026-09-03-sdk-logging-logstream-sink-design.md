# Secco.SDK.Logging — o sink `ILogger` → LogStream (`AddLogStream()`) — design

**Data:** 2026-09-03
**Status:** aprovado para planejamento
**Item de origem:** issue [#1](https://github.com/rafsecco/secco-platform/issues/1) (`adopter-demand`,
`blocker`) — o provider prometido pela ADR-0008 nunca foi implementado. Trava o item
"Integração com `Secco.LogStream.Client` (logs)" da Fase 0 do roadmap do `secco-intranet`.

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
   registrado é o log de decisões de design (as quatro escolhas acima e as alternativas descartadas).

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

O `ServiceName` entra no início da mensagem como prefixo estruturado (`[serviço] mensagem`) até
existir campo próprio no `LogEntry`. Isso é dívida consciente e está anotada abaixo.

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

## Escopo

**Dentro:**

- Pacote `Secco.SDK.Logging` completo (provider, logger, fila, dispatcher, options, `AddLogStream()`).
- `SeccoAmbientContext` no `Secco.SDK.AspNetCore`, alimentado pelos middlewares e por `SetTenant`.
- Promoção do handler de client credentials para o SDK e adaptação do `Secco.SecureGate.Client`.
- `CorrelationId?` em `CreateLogEntryRequest` + handler + `openapi.json` + client regenerados.
- Testes (abaixo), README do pacote, entrada no `publish-packages.yml`, `docs/roadmap.md`.

**Fora (registrado, não feito):**

- Campo próprio de serviço/categoria no `LogEntry` — é mudança de schema do LogStream e merece a
  mesma rodada que a issue #2 (ator no `LogEntry`) vai forçar. Até lá, prefixo na mensagem.
- Escopos do `ILogger` (`BeginScope`) como dado estruturado — o `LogEntry` não tem onde guardar.
- OpenTelemetry (a outra metade da ADR-0008) — não é o que a issue #1 pede.

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
- Teste de contrato `OpenApiContractTests` atualizado com `SECCO_UPDATE_OPENAPI=true`.

## Riscos

- **Promoção do handler mexe em código publicado.** `Secco.SecureGate.Client` 0.x tem o tipo como
  `internal`, então mover não quebra consumidor externo; o risco é de regressão silenciosa no
  catálogo e na resolução de permissões. Mitigação: os testes existentes do SecureGate cobrem os
  dois caminhos e devem passar sem alteração.
- **`AsyncLocal` mal posicionado vaza contexto entre requisições.** Escrito dentro do
  `InvokeAsync` do middleware, o valor flui para baixo e não sobe. Mitigação: teste de integração
  com duas requisições concorrentes de tenants diferentes.
