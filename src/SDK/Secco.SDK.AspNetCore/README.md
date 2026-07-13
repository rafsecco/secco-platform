# Secco.SDK.AspNetCore

Comportamento transversal de runtime da Secco Platform (ADR-0004): nenhum produto reimplementa correlação, tenancy, resiliência ou health checks localmente.

## Uso recomendado — composição completa

```csharp
builder.Services.AddSeccoPlatform();   // correlation + tenancy + health + resilience

var app = builder.Build();
app.UseSeccoPlatform();                // ordem correta: correlation → [auth, Fase 6] → tenancy
app.MapSeccoPlatform();                // /health/live e /health/ready
```

Chamadas repetidas de `AddSeccoPlatform()` são no-op. Ajuste fino: chamar a extensão individual (ex.: `AddSeccoResilience(o => ...)`) **antes** — a composição não duplica o que já existe. `AddSeccoAuthentication()` entrará no agregado quando o SecureGate existir (Fase 6), de forma aditiva.

## Disponível nesta versão

- `AddSeccoCorrelation()` / `UseSeccoCorrelation()` — propaga `X-Correlation-Id` (constante `SeccoHeaders.CorrelationId` do SharedKernel) por toda a requisição e a devolve no header de resposta.
- `AddSeccoTenancy()` / `UseSeccoTenancy()` — resolve o tenant (claim `tenant_id` primária; header `X-Tenant-Id` só sem claim; divergência = 400) e expõe `ITenantContext` + `ITenantConnectionFactory` (ADR-0005).
- `AddSeccoHealthChecks()` / `MapSeccoHealthChecks()` — `/health/live` (processo vivo, nenhum check) e `/health/ready` (todos os checks, JSON sem detalhes sensíveis).
- `AddSeccoResilience()` — pipeline padrão de resiliência (retry + circuit breaker + timeouts) em todo `HttpClient`; retry automático só para métodos idempotentes.
- `AddSeccoAuthentication()` — JWT Bearer conforme ADR-0007: claims curtas sem remapeamento (`sub`/`role`/`tenant_id`/`scope`), `FallbackPolicy` fail-closed; Authority OIDC ou chave HS256 de desenvolvimento (proibida em Production).
- `AddSeccoPlatform()` / `UseSeccoPlatform()` / `MapSeccoPlatform()` — composição de tudo acima, com ordem de pipeline fixada (correlation → auth → tenancy) e guarda contra registro duplicado.

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

As exceções de tenancy viram ProblemDetails no pipeline (`UseSeccoTenancy()` inclui o middleware de tradução): `TenantNotResolvedException` e `TenantNotFoundException` → **400** (sem ecoar o identificador recebido); `TenantCatalogUnavailableException` → **503 + Retry-After** (condição transitória — o retry da plataforma se recupera sozinho). Qualquer outra exceção segue intocada — não é um exception handler global.

Fora de DEV, o catálogo por configuração dá lugar ao catálogo central servido pelo **SecureGate**: o pacote `Secco.SecureGate.Client` traz `AddSecureGateTenantCatalog()` (seção `Secco:SecureGate`), com cache por TTL e stale em falha — ver o README do SecureGate.

## Autenticação (ADR-0007)

Configuração pela seção `Secco:Authentication`, validada no startup — fail-fast (ADR-0020):

```json
"Secco": { "Authentication": {
    "Audience": "secco-logstream",
    "Authority": "https://securegate..."          // produção (OIDC/JWKS)
    // OU, fora de Production:
    // "Issuer": "secco-dev",
    // "DevelopmentSigningKey": "<mín. 32 chars>"  // HS256 local até o SecureGate existir
} }
```

Regras aplicadas centralmente: mapeamento automático de claims **desligado** (`MapInboundClaims = false`), `NameClaimType = "sub"`, `RoleClaimType = "role"`; `Authority` e `DevelopmentSigningKey` mutuamente exclusivos; chave de desenvolvimento em Production = startup falha. A `FallbackPolicy` exige usuário autenticado em todo endpoint sem metadata explícita — exceções (`AllowAnonymous`) são explícitas e auditáveis; os health checks já vêm anônimos do SDK.

## Autorização granular (ADR-0021)

Incluída no `AddSeccoPlatform()`. O token carrega apenas `role`; as permissões (`recurso:acao`) do par `(tenant, role)` são resolvidas em runtime. O endpoint declara a permissão direto como policy — o nome no formato canônico (validado pelo `SeccoPermissions` do kernel) vira uma policy dinâmica, sem registro:

```csharp
group.MapPost("/", ...)
    .RequireAuthorization(LogStreamPermissions.LogEntries.Write);   // "log-entries:write"
```

As constantes de permissão vivem em **cada produto** (regra de admissão da ADR-0003 — o exemplo da ADR-0021 é lido como padrão, não como localização). Resolução com **cache obrigatório** por `(tenant_id, role)` (`Secco:Authorization:CacheTtlSeconds`, padrão 60s) — **estrito e fail-closed**: expirado + resolver indisponível = acesso negado; autorização nunca falha aberta, e o TTL é o teto da janela de revogação. Resolver padrão lê de configuração (DEV/testes, global — sem semântica por tenant):

```json
"Secco": { "Authorization": { "Roles": {
    "dev-admin": { "Permissions": [ "log-entries:read", "log-entries:write" ] }
} } }
```

Fora de DEV, `AddSecureGatePermissionResolver()` (pacote `Secco.SecureGate.Client`) resolve no SecureGate com scope `authorization:read` — ver o README do SecureGate. Ordem de pipeline: `UseSeccoPlatform()` posiciona a tenancy **antes** da autorização — as policies de permissão precisam do tenant resolvido.

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
