# Secco.SDK.AspNetCore

Comportamento transversal de runtime da Secco Platform (ADR-0004): nenhum produto reimplementa correlação, tenancy, resiliência ou health checks localmente.

## Disponível nesta versão

- `AddSeccoCorrelation()` / `UseSeccoCorrelation()` — propaga `X-Correlation-Id` (constante `SeccoHeaders.CorrelationId` do SharedKernel) por toda a requisição e a devolve no header de resposta.

## Uso

```csharp
builder.Services.AddSeccoCorrelation();
// ...
app.UseSeccoCorrelation();
```

```csharp
public sealed class SomeHandler(ICorrelationContext correlationContext)
{
    public void Handle() => _logger.LogInformation("Processando {CorrelationId}", correlationContext.CorrelationId);
}
```

Regras de confiança (ADR-0020): um `X-Correlation-Id` recebido só é reaproveitado se for um `Guid` válido e não vazio; caso contrário um novo `Guid` v7 é gerado — nunca se propaga um valor de entrada não validado.
