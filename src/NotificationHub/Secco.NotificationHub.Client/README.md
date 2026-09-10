# Secco.NotificationHub.Client

Client HTTP do Secco.NotificationHub, **gerado por NSwag** a partir do [`openapi.json`](../Secco.NotificationHub.Api/openapi/openapi.json) versionado (ADR-0006). É a única forma de comunicação entre produtos da plataforma — `HttpClient` manual é proibido.

O código do client é gerado durante o build (`NSwag.ApiDescription.Client`); mudou o contrato → o snapshot é atualizado e o client regenera no mesmo PR. Nunca editar código gerado — ajustes vão em arquivos parciais.

## Uso

```csharp
builder.Services.AddNotificationHubClient(options => options.BaseUrl = "https://notificationhub.exemplo.com");

public sealed class MeuServico(INotificationHubClient notificationHub)
{
    // métodos tipados gerados do contrato
}
```

Combinado com `AddSeccoResilience()` no host, o client herda retry/circuit breaker/timeouts automaticamente (ADR-0004).

## Autenticação (issue #25)

Todo endpoint do NotificationHub exige permissão (ADR-0021) — sem credencial configurada, a primeira chamada tomaria 401. `AddNotificationHubClient()` anexa automaticamente um handler de client credentials quando encontra as credenciais, lidas da seção `Secco:NotificationHub` com fallback para `Secco:SecureGate`:

```jsonc
{
  "Secco": {
    "NotificationHub": {
      "BaseUrl": "https://notificationhub.exemplo.com"
      // IssuerBaseUrl, ClientId e ClientSecret caem para Secco:SecureGate quando ausentes
    }
  }
}
```

- **Sem nenhuma credencial** (nem por fallback): nenhum handler é anexado — serve DEV com um token HS256 emitido fora da plataforma.
- **Credenciais parciais**: falha rápido (`InvalidOperationException`) — nunca vira client sem autenticação em silêncio (ADR-0020).
- `IssuerBaseUrl`/`ClientId`/`ClientSecret` também podem ser definidos em código via `AddNotificationHubClient(o => { ... })`; quando presentes, vencem os lidos da configuração.
- `Scope` tem o padrão `NotificationHubClientOptions.DefaultScope` (`"notificationhub"`) — não é preciso conhecer o valor nem informá-lo.
