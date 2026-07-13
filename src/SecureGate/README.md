# Secco.SecureGate

Identity & Access Management da Secco Platform (ADR-0022): servidor OIDC (OpenIddict + ASP.NET Identity) com identidade como **dado de plataforma** — banco próprio `secco_securegate` (ADR-0017 completa) com usuários/roles por tenant, clients OIDC e o **catálogo de tenants** da plataforma (ADR-0005). Nasceu do template `secco-service` (Fase 6.1) e nenhum outro produto acessa este banco: a integração é somente via tokens (JWKS) e via `Secco.SecureGate.Client`.

## O que está entregue

### Servidor OIDC — client credentials + JWKS (Fase 6.2)

- `POST /connect/token` (client credentials), discovery em `/.well-known/openid-configuration`.
- Access tokens são **JWT puros assinados** (sem criptografia de conteúdo): os produtos validam com o `JwtBearer` padrão do SDK via Authority/JWKS — nenhum produto conhece o OpenIddict e não há chave simétrica compartilhada.
- Claims curtas (ADR-0007): `sub` = client id, `scope`, audience derivada do scope (`logstream` → `secco-logstream`).
- Certificados por ambiente: efêmeros em Testing, certificado de desenvolvimento em DEV/Staging; em **Production, `SecureGate:Signing:CertificatePath` é obrigatório — sem ele a API não sobe** (fail-fast, ADR-0020).
- Scopes semeados como **seed de referência** (ADR-0019); tenant demo + client `secco-dev-console` em seed de DEV (guarda dupla).

### Catálogo de tenants (Fase 6.3)

O catálogo central que substitui o catálogo por configuração dos produtos fora de DEV. Modelo: `Tenant` + `TenantDatabase` — **uma connection string por (tenant, produto)**, refletindo o database-per-tenant por produto da ADR-0005.

**Gestão** (`/api/v1/tenants`, scope `securegate:admin` — AdminPortal/operadores):

| Endpoint | Efeito |
|---|---|
| `POST /api/v1/tenants` | Cria tenant (nasce ativo) |
| `GET /api/v1/tenants` · `GET /{id}` | Lista/consulta (NUNCA devolve connection strings) |
| `PUT /api/v1/tenants/{id}/databases/{product}` | Cadastra/rotaciona o banco do tenant no produto (write-only) |
| `POST /api/v1/tenants/{id}/activate` · `/deactivate` | Liga/desliga o tenant no catálogo |

**Catálogo** (`/api/v1/catalog/{product}/tenants[/{tenantId}]`, scope `catalog:<produto>`): a única superfície que entrega connection strings — e só as do produto do próprio scope (least privilege, ADR-0020): o client do LogStream carrega `catalog:logstream` e não lê o catálogo de nenhum outro produto. Tenants desativados somem do catálogo; desconhecido, desativado e sem banco respondem o mesmo 404.

### `Secco.SecureGate.Client` — catálogo remoto para os produtos

Pacote NSwag (ADR-0006) que além do `ISecureGateClient` traz o `SecureGateTenantCatalog : ITenantCatalog` pronto:

```csharp
builder.Services.AddSeccoPlatform();
builder.Services.AddSecureGateTenantCatalog();   // decisão por configuração, lazy
```

```json
"Secco": { "SecureGate": {
    "BaseUrl": "https://securegate.interno",
    "ClientId": "logstream-service",
    "ClientSecret": "<segredo>",
    "Product": "logstream",
    "CacheTtlSeconds": 300
} }
```

- **Sem a seção**: o produto segue no catálogo por configuração do SDK — DEV não muda. Seção **parcial** falha rápido no primeiro uso (nunca degrada em silêncio).
- Token via client credentials com o scope `catalog:<produto>` apenas, renovado automaticamente.
- Cache in-memory por tenant com TTL curto e **stale em falha**: SecureGate indisponível não derruba produto que já resolveu o tenant (warning no log); tenant nunca visto com SecureGate fora = `TenantCatalogUnavailableException` → **503 + Retry-After** pelo pipeline de tenancy do SDK. Desativação de tenant propaga em até um TTL.
- Connection strings jamais aparecem em logs ou erros (ADR-0020).

## Configuração

| Seção | Uso |
|---|---|
| `SecureGate:Database` | `Provider` (`SqlServer` padrão / `Postgres`, ADR-0018) + `ConnectionString` do banco de plataforma |
| `SecureGate:Tokens:AccessTokenLifetimeMinutes` | Vida do access token (padrão 60) |
| `SecureGate:Signing:CertificatePath/CertificatePassword` | Certificado PKCS#12 — obrigatório em Production |

## Testes

`tests/SecureGate/Secco.SecureGate.Tests` — Testcontainers (ADR-0012): schema ADR-0017 provado por INFORMATION_SCHEMA, fluxo client credentials/JWKS, gestão e catálogo com autorização por scope, cache TTL/stale do client e os E2E cross-produto: o LogStream valida token emitido pelo SecureGate **e resolve tenant pelo catálogo remoto sem nenhum tenant em configuração** (inclusive migrations via `ListAsync`).

## Próximas fases

- **6.4** — `AddSeccoAuthorization()` (ADR-0021): role→permissions por tenant, cache curto, fail-closed.
- **6.5** — Login de usuário: authorization code + PKCE + telas (para o AdminPortal, Fase 7).
