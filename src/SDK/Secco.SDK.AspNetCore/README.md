# Secco.SDK.AspNetCore

Comportamento transversal de runtime da Secco Platform (ADR-0004): nenhum produto reimplementa correlação, tenancy, resiliência ou health checks localmente.

## Disponível nesta versão

- `AddSeccoCorrelation()` / `UseSeccoCorrelation()` — propaga `X-Correlation-Id` (constante `SeccoHeaders.CorrelationId` do SharedKernel) por toda a requisição e a devolve no header de resposta.
- `AddSeccoTenancy()` / `UseSeccoTenancy()` — resolve o tenant (claim `tenant_id` primária; header `X-Tenant-Id` só sem claim; divergência = 400) e expõe `ITenantContext` + `ITenantConnectionFactory` (ADR-0005).
- `AddSeccoHealthChecks()` / `MapSeccoHealthChecks()` — `/health/live` (processo vivo, nenhum check) e `/health/ready` (todos os checks, JSON sem detalhes sensíveis).
- `AddSeccoResilience()` — pipeline padrão de resiliência (retry + circuit breaker + timeouts) em todo `HttpClient`; retry automático só para métodos idempotentes.

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

## Health checks (ADR-0004)

```csharp
builder.Services.AddSeccoHealthChecks()
    .AddCheck<MinhaDepend>("fila");   // checks do produto afetam só o /health/ready
// ...
app.MapSeccoHealthChecks();
```

`/health/live` não executa nenhum check — reiniciar o processo não conserta dependência externa caída. `/health/ready` executa todos os checks e responde JSON com nome, status e duração por check — **sem** descrições nem mensagens de exceção (ADR-0020: erros de infraestrutura vazam hostnames e connection strings; diagnóstico detalhado fica nos logs). Endpoints anônimos: probes de orquestrador não autenticam.

## Resiliência (ADR-0004)

```csharp
builder.Services.AddSeccoResilience();          // padrões da plataforma
// ou, com ajuste fino:
builder.Services.AddSeccoResilience(o => o.Retry.MaxRetryAttempts = 5);
```

Aplica o `AddStandardResilienceHandler` da Microsoft (rate limiter → timeout total 30s → retry 3x exponencial com jitter → circuit breaker → timeout 10s/tentativa) a **todo** `HttpClient` registrado via `AddHttpClient` — incluindo os clients NSwag dos produtos (ADR-0006), sem opt-in. Retry automático **só para métodos idempotentes** (RFC 9110: GET/HEAD/PUT/DELETE/OPTIONS/TRACE): repetir POST/PATCH após timeout pode duplicar o efeito no servidor — eles seguem protegidos por timeout e circuit breaker. Quando a plataforma tiver idempotency keys (ADR futura), a regra será revista.
