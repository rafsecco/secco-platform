# Secco.SDK.AspNetCore

Comportamento transversal de runtime da Secco Platform (ADR-0004): nenhum produto reimplementa correlação, tenancy, resiliência ou health checks localmente.

## Disponível nesta versão

- `AddSeccoCorrelation()` / `UseSeccoCorrelation()` — propaga `X-Correlation-Id` (constante `SeccoHeaders.CorrelationId` do SharedKernel) por toda a requisição e a devolve no header de resposta.
- `AddSeccoTenancy()` / `UseSeccoTenancy()` — resolve o tenant (claim `tenant_id` primária; header `X-Tenant-Id` só sem claim; divergência = 400) e expõe `ITenantContext` + `ITenantConnectionFactory` (ADR-0005).

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

## Tenancy (ADR-0005)

```csharp
builder.Services.AddSeccoTenancy();
// ...
app.UseAuthentication();   // a claim tenant_id é a fonte primária
app.UseSeccoTenancy();
```

Catálogo padrão via configuração (substituível registrando outro `ITenantCatalog` antes de `AddSeccoTenancy()`):

```json
"Secco": { "Tenancy": { "Tenants": {
    "<guid-do-tenant>": { "ConnectionString": "..." }
} } }
```

Regras de confiança (ADR-0020): claim assinada vence sempre; header `X-Tenant-Id` só é considerado sem claim e se for `Guid` válido; claim e header divergentes → 400 (possível tentativa cross-tenant, logada como warning); claim presente porém inválida **não** cai para o header. O middleware não bloqueia requisições sem tenant (health checks funcionam) — a barreira é o `ITenantConnectionFactory`, que lança `TenantNotResolvedException` sem tenant resolvido.
