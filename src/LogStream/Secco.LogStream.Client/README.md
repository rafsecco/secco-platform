# Secco.LogStream.Client

Client HTTP do Secco.LogStream, **gerado por NSwag** a partir do [`openapi.json`](../Secco.LogStream.Api/openapi/openapi.json) versionado (ADR-0006). É a única forma de comunicação entre produtos da plataforma — `HttpClient` manual é proibido.

O código do client é gerado durante o build (`NSwag.ApiDescription.Client`); mudou o contrato → o snapshot é atualizado e o client regenera no mesmo PR. Nunca editar código gerado — ajustes vão em arquivos parciais.

## Uso

```csharp
builder.Services.AddLogStreamClient(options => options.BaseUrl = "https://logstream.exemplo.com");

public sealed class MeuServico(ILogStreamClient logStream)
{
    // métodos tipados gerados do contrato
}
```

Combinado com `AddSeccoResilience()` no host, o client herda retry/circuit breaker/timeouts automaticamente (ADR-0004).

## Autenticação (issue #25)

Todo endpoint do LogStream exige permissão (ADR-0021) — sem credencial configurada, a primeira chamada tomaria 401. `AddLogStreamClient()` anexa automaticamente um handler de client credentials quando encontra as credenciais, lidas da seção `Secco:LogStream` com fallback para `Secco:SecureGate`:

```jsonc
{
  "Secco": {
    "LogStream": {
      "BaseUrl": "https://logstream.exemplo.com"
      // IssuerBaseUrl, ClientId e ClientSecret caem para Secco:SecureGate quando ausentes
    }
  }
}
```

- **Sem nenhuma credencial** (nem por fallback): nenhum handler é anexado — serve DEV com um token HS256 emitido fora da plataforma.
- **Credenciais parciais**: falha rápido (`InvalidOperationException`) — nunca vira client sem autenticação em silêncio (ADR-0020).
- `IssuerBaseUrl`/`ClientId`/`ClientSecret` também podem ser definidos em código via `AddLogStreamClient(o => { ... })`; quando presentes, vencem os lidos da configuração.
- `Scope` tem o padrão `LogStreamClientOptions.DefaultScope` (`"logstream"`) — não é preciso conhecer o valor nem informá-lo.
