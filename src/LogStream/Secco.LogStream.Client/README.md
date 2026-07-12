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
