# Secco.SDK.Logging

Sink `ILogger` → `Secco.LogStream` da Secco Platform (ADR-0008). `AddLogStream()` deixa qualquer produto escrever com `ILogger<T>` normalmente e ter os logs enviados ao LogStream com tenant, correlação, serviço e categoria enriquecidos automaticamente — lote + fila local, sem nunca bloquear o request. Resolve a issue [#1](https://github.com/rafsecco/secco-platform/issues/1).

Não confundir com `AddLogStreamClient()`, do pacote `Secco.LogStream.Client`: aquele registra o client HTTP para quem quer **consultar** o LogStream ou escrever nele por conta própria. Este liga o `ILogger<T>` que o produto já usa à ingestão, sem o produto escrever uma linha de código de log. Os dois convivem no mesmo host.

## Uso

```csharp
builder.Services.AddSeccoPlatform();
builder.Services.AddLogStream();   // lê a seção Secco:LogStream
```

```jsonc
{
  "Secco": {
    "LogStream": {
      "BaseUrl": "https://logstream.interno",
      "MinimumLevel": "Information"
      // AuthorityUrl, ClientId e ClientSecret caem para a seção Secco:SecureGate
      // quando não declarados aqui — a maioria dos produtos já a configura.
    }
  }
}
```

Nada mais muda no produto: `_logger.LogError(exception, "Falha ao publicar {DocumentoId}", id)` continua sendo a única forma de logar.

## Opções (`Secco:LogStream`)

| Chave | Padrão | O que faz |
| --- | --- | --- |
| `Enabled` | `true` | Desliga o envio por completo, sem remover o `AddLogStream()`. |
| `BaseUrl` | — | API do LogStream. Obrigatória com o sink ligado. |
| `AuthorityUrl` | `Secco:SecureGate:BaseUrl` | Emissor do token de máquina. |
| `ClientId` / `ClientSecret` | `Secco:SecureGate:*` | Credenciais client credentials. O segredo nunca é logado. |
| `Scope` | `logstream` | Scope solicitado (audience `secco-logstream`). |
| `ServiceName` | assembly de entrada | Vai na coluna `ds_service_name` de cada entrada. |
| `PlatformTenantId` | — | Destino dos logs emitidos **sem tenant**. Sem ele, essas entradas são descartadas. |
| `MinimumLevel` | `Information` | Nível mínimo enviado. |
| `QueueCapacity` | `10000` | Fila local; cheia, descarta e conta. |
| `BatchSize` / `FlushIntervalMs` | `100` / `2000` | Fecha o lote pelo que vier primeiro. |
| `ShutdownFlushTimeoutMs` | `5000` | Prazo do flush final no encerramento. |
| `MaxMessageLength` / `MaxStackTraceLength` | `8000` / `16000` | Truncamento antes da fila (ADR-0020). |

## Nota operacional — permissão em cada tenant

O client OIDC do produto precisa de um role com `log-entries:write` **em cada tenant** para o qual ele loga (a resolução de permissões é por `(tenant_id, role)`, ADR-0021). Sem essa permissão o LogStream responde 403 e o lote inteiro é descartado — silenciosamente do ponto de vista do produto, porque o sink nunca lança; o fato aparece no log local do dispatcher, não como exceção no request.

## O que o sink deliberadamente não faz

- **Não bloqueia e não lança.** Fila cheia descarta e conta; LogStream fora do ar perde o lote. É a decisão da ADR-0008: log indisponível não derruba produto. Log que você não pode perder não é log — é auditoria, e para isso existe `/api/v1/audit-entries`, que grava de forma síncrona.
- **Não envia log sem tenant**, a menos que `PlatformTenantId` esteja configurado: o LogStream é database-per-tenant (ADR-0005) e sem tenant não há banco de destino.
- **Não suporta `BeginScope`.** O `LogEntry` do LogStream não tem onde guardar dado estruturado de escopo; prometer suporte seria enganar quem usa.
- **Não envia as próprias categorias de transporte** (`System.Net.Http.*`, `Microsoft.Extensions.Http.*`, `Polly*`, `Secco.SDK.Logging.*`). Sem essa guarda, cada envio de lote geraria logs que provocariam outro envio: um laço que nunca converge.
- **Não carimba o horário de emissão.** O `dt_created_at` é o do servidor, no momento da ingestão — entre logar e chegar lá passa, no pior caso, um `FlushIntervalMs`.
