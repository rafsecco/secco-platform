# Secco.SDK.AspNetCore

Comportamento transversal de runtime da Secco Platform (ADR-0004): nenhum produto reimplementa correlação, tenancy, resiliência ou health checks localmente.

## Uso recomendado — composição completa

```csharp
builder.Services.AddSeccoPlatform();   // correlation + auth + tenancy + authorization + health + resilience

var app = builder.Build();
app.UseSeccoPlatform();                // ordem fixa: correlation → auth → tenancy → authorization
app.MapSeccoPlatform();                // /health/live e /health/ready
```

Chamadas repetidas de `AddSeccoPlatform()` são no-op. Ajuste fino: chamar a extensão individual (ex.: `AddSeccoResilience(o => ...)`) **antes** — a composição não duplica o que já existe.

## Disponível nesta versão

- `AddSeccoCorrelation()` / `UseSeccoCorrelation()` — propaga `X-Correlation-Id` (constante `SeccoHeaders.CorrelationId` do SharedKernel) por toda a requisição e a devolve no header de resposta.
- `AddSeccoTenancy()` / `UseSeccoTenancy()` — resolve o tenant (claim `tenant_id` primária; header `X-Tenant-Id` só sem claim; divergência = 400) e expõe `ITenantContext` + `ITenantConnectionFactory` (ADR-0005).
- `AddSeccoHealthChecks()` / `MapSeccoHealthChecks()` — `/health/live` (processo vivo, nenhum check) e `/health/ready` (todos os checks, JSON sem detalhes sensíveis).
- `AddSeccoResilience()` — pipeline padrão de resiliência (retry + circuit breaker + timeouts) em todo `HttpClient`; retry automático só para métodos idempotentes.
- `AddSeccoAuthentication()` — JWT Bearer conforme ADR-0007: claims curtas sem remapeamento (`sub`/`role`/`tenant_id`/`scope`), `FallbackPolicy` fail-closed; Authority OIDC ou chave HS256 de desenvolvimento (proibida em Production).
- `AddSeccoAuthorization()` — autorização granular Role + Permission (ADR-0021): policies dinâmicas por permissão (`RequireAuthorization("recurso:acao")`), resolução `(tenant, role) → permissões` com cache de TTL curto e **fail-closed**. Resolver por `IConfiguration` (DEV) ou remoto via `Secco.SecureGate.Client`.
- `AddSeccoOpenApi()` — OpenAPI com as convenções da plataforma (enums como `type: string`, ADR-0006).
- `AddSeccoBackgroundJobs()` — jobs persistentes com retry automático (ADR-0015 Camada 2): `IBackgroundJobScheduler` sobre Hangfire, com o tenant restaurado automaticamente na execução. **Não** faz parte de `AddSeccoPlatform()` — opt-in só para quem precisa (produtos isolados não carregam Hangfire).
- `AddSeccoSecurityHeaders()` / `UseSeccoSecurityHeaders()` — cabeçalhos de segurança de resposta (ADR-0057): CSP com nonce por requisição, `nosniff`, `Referrer-Policy`, `Permissions-Policy`, `frame-ancestors`/`X-Frame-Options` e HSTS. **Não** faz parte de `UseSeccoPlatform()` — opt-in para quem serve HTML.
- `AddSeccoPlatform()` / `UseSeccoPlatform()` / `MapSeccoPlatform()` — composição de tudo acima, com ordem de pipeline fixada (correlation → auth → tenancy → authorization) e guarda contra registro duplicado.

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

## OpenAPI (ADR-0006)

```csharp
builder.Services.AddSeccoOpenApi();   // drop-in do AddOpenApi() com as convenções da plataforma
```

Convenção atual: enums serializados como string (`JsonStringEnumConverter`) são declarados no schema com `type: string`. Sem isso, o .NET 10 emite os valores string mas **omite o tipo**, e o gerador de client (NSwag) assume enum numérico — quebrando a desserialização no consumidor. Corrige o contrato **na fonte** (o client gerado passa a emitir `[JsonConverter(typeof(JsonStringEnumConverter))]`), sem correções manuais no código gerado.

## Background jobs (ADR-0015 Camada 2)

```csharp
builder.Services.AddSeccoBackgroundJobs(serviceProvider =>
{
    // Resolve a connection string do banco de PLATAFORMA do produto (nunca por tenant) —
    // lazy: roda quando o Hangfire inicializa de fato, depois de toda a configuração
    // (inclusive a injetada por testes) já estar montada.
    return serviceProvider.GetRequiredService<IConfiguration>()["MeuProduto:BackgroundJobs:ConnectionString"]!;
});
```

```csharp
public sealed class SendEmailJob(INotificationRepository repository, ISeccoEmailSender sender) : IBackgroundJob<SendEmailPayload>
{
    public async Task ExecuteAsync(SendEmailPayload payload, CancellationToken cancellationToken)
    {
        // O tenant já está restaurado no escopo — o job nunca chama SetTenant sozinho.
        var entity = await repository.GetByIdAsync(payload.NotificationId, cancellationToken);
        // ...
    }
}

// scheduler.Enqueue<SendEmailJob, SendEmailPayload>(tenantId, new SendEmailPayload(id));
```

Regras (ADR-0015): jobs vivem no banco de **plataforma** do produto — nunca por tenant; o `tenant_id` viaja como argumento e o `TenantJobRunner` interno restaura o contexto (ADR-0005) ANTES de resolver e invocar o job via DI, então qualquer repositório usado dentro do job já enxerga o tenant certo. Retry automático fixo em `[AutomaticRetry(Attempts = 5)]` no runner — é decisão de plataforma, não por job; um produto que precise de outra política ajusta via `GlobalJobFilters` no próprio host. Núcleo gratuito do Hangfire (LGPL) apenas — recursos Pro exigem reavaliar a ADR-0015 antes de adotar. Produtos que não precisam de jobs persistentes não chamam esta extensão — não entra em `AddSeccoPlatform()` ("produtos isolados permanecem leves").

**Testes com múltiplas `WebApplicationFactory`**: o Hangfire usa um bridge de log estático por processo (`Hangfire.Logging.LogProvider`) — se cada classe de teste criar sua própria factory com Hangfire, a primeira a ser descartada deixa as demais com um `ILoggerFactory` já disposto (`ObjectDisposedException` no primeiro job enfileirado). Compartilhe uma única factory entre as classes de teste do produto via `ICollectionFixture` (ver `Secco.NotificationHub.Tests` para o padrão).

## Cabeçalhos de segurança (ADR-0057)

Para aplicação que serve HTML (relying party, console de operação, telas de login). Os padrões já são seguros; cada ajuste é opção nomeada, validada na subida.

```csharp
builder.Services.AddSeccoSecurityHeaders(options => options
    .DenyFraming()                                   // frame-ancestors 'none' + X-Frame-Options: DENY
    .AllowStyleAttribute("--sc-setor-hue:152"));     // só este valor exato de style="..."

app.UseExceptionHandler("/erro");
app.UseSeccoSecurityHeaders();                       // antes de UseStaticFiles, para cobri-los também
app.UseStaticFiles();
```

Nas views (MVC e Razor Pages), com `@addTagHelper *, Secco.SDK.AspNetCore` no `_ViewImports.cshtml`:

```html
<script secco-nonce>/* inline */</script>
<style secco-nonce>/* inline */</style>
```

Em componente Razor renderizado no servidor, injete `ISeccoCspNonce` e use `nonce="@Nonce.Value"`. Em código, `HttpContext.GetSeccoCspNonce()`.

**O que sai sem configuração:**

| Cabeçalho | Valor |
|---|---|
| `Content-Security-Policy` | `default-src 'self'; script-src 'self' 'nonce-…'; style-src 'self' 'nonce-…'; style-src-attr 'none'; img-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'self'` |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Permissions-Policy` | nega câmera, microfone, geolocalização, pagamento, USB, serial, MIDI, captura de tela e sensores |
| `X-Frame-Options` | coerente com `frame-ancestors`: `DENY` para `'none'`, `SAMEORIGIN` para `'self'`, ausente para outras origens |
| `Strict-Transport-Security` | `max-age=31536000`, fora de Development e de `localhost` — **também por HTTP**, porque o navegador o ignora sem TLS e assim ele chega atrás de proxy que termina o TLS |

**Ajustes:** `ScriptSources`, `StyleSources`, `ImageSources` (aceita `blob:`), `FontSources` (aceita `data:`), `ConnectSources`, `FrameAncestors`, `FormActions`, `AllowedPermissions`, `AllowStyleAttribute(valor)`, `Mode` (`Enforce` ou `ReportOnly`), `ReportUri` (coletor externo HTTPS; o SDK não hospeda coletor) e as opções de HSTS.

**Recusado na subida (fail-fast):** qualquer palavra-chave além de `'self'` (logo, `'unsafe-inline'`, `'unsafe-eval'`, `'unsafe-hashes'` e `'strict-dynamic'`), `*`, `http:`, `data:` em script ou estilo, e origem com `;`, vírgula, aspas ou espaço — esses caracteres injetariam conteúdo na CSP.

Regras que valem saber:

- **Atributo `style="..."` não aceita nonce.** Ou vira classe no CSS (preferível), ou entra por `AllowStyleAttribute` com o valor **exato** já decodificado, que vira `'unsafe-hashes' 'sha256-…'`. Não existe opção que libere atributo de estilo qualquer.
- **`form-action` fica fora do padrão.** O navegador aplica a diretiva também aos redirects que seguem o POST, e o login e o logout por OIDC terminam em outra origem. Declare as origens que o produto conhece em `FormActions`.
- **Resposta que usa o nonce sai com `Cache-Control: no-store`** e fica fora do output cache: um nonce reaproveitado seria um valor que o atacante lê e usa.
- **Em `ReportOnly`, o navegador ignora `frame-ancestors`.** O `X-Frame-Options` continua protegendo contra moldura.
- **Blazor Server:** o diálogo padrão de reconexão injeta `<style>` sem nonce e sai sem estilo. Declare o seu próprio, com `id="components-reconnect-modal"` e CSS na folha da aplicação.
