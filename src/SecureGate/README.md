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

### Roles + permissões (Fase 6.4, ADR-0021)

O token carrega apenas `role`; o mapeamento role→permissions é **por tenant** e vive aqui (padrão Identity: `tb_roles` + claims de ação em `tb_role_claims`). **Clients OIDC também têm roles** (`ds_roles`, emitidos na claim curta `role` do client credentials) — máquinas e usuários no MESMO modelo de autorização, sem caso especial.

**Gestão** (`/api/v1/tenants/{tenantId}/roles`, scope `securegate:admin`): criar role, listar com permissões e `PUT /{role}/permissions` **idempotente** (revogar = enviar o conjunto sem a permissão — propaga aos produtos em ≤ 1 TTL de cache, com o token ainda válido).

**Resolução** (`/api/v1/authorization/tenants/{tenantId}/roles/{role}/permissions`, scope único `authorization:read`): a fonte do `IPermissionResolver` remoto do SDK. Role desconhecido responde lista vazia — equivalente para autorização, e não revela o modelo de roles do tenant (ADR-0020).

No produto consumidor:

```csharp
builder.Services.AddSecureGatePermissionResolver();   // mesma seção Secco:SecureGate; Product não é exigido
```

Sem a seção, o resolver por configuração do SDK segue valendo (DEV). O token de autorização é separado do token do catálogo — cada recurso pede só o próprio scope (least privilege).

### Login de usuário (Fase 6.5, ADR-0022)

Fluxo **authorization code + PKCE** (obrigatório, inclusive para clients públicos) + refresh token. ASP.NET Identity via `AddIdentityCore` com cookie **não-default** — o esquema padrão da API segue sendo o JwtBearer da ADR-0007; o cookie serve só às telas e ao `/connect/authorize`.

| Endpoint | Papel |
|---|---|
| `/connect/authorize` | Sem sessão → desafia o cookie → tela de login; autenticado → emite o authorization code |
| `/connect/token` (`authorization_code`/`refresh_token`) | Troca o code/refresh por tokens, **re-derivando as claims do banco** a cada emissão |
| `/connect/userinfo` | Claims do usuário conforme os scopes concedidos |
| `/connect/logout` | Encerra a sessão (cookie) + end-session OIDC |
| `/login` (Razor Page) | Tela server-rendered, self-contained, antiforgery |

- **Tenant no login**: email/username é único **global** — o usuário digita e-mail + senha e o tenant vem do registro (`User.TenantId`); sem seletor de tenant.
- **Provisionamento** (`POST /api/v1/tenants/{tenantId}/users`, scope `securegate:admin`): usuários são criados por administradores (hash de senha do Identity, roles atribuídos no tenant), **sem auto-registro público** (ADR-0020). Senhas nunca voltam nas respostas.
- **Segurança**: PKCE obrigatório, bloqueio contra força bruta (lockout do Identity), `LocalRedirect` (sem open redirect), mensagens de login genéricas (sem enumeração de e-mail), re-derivação de claims no refresh (usuário desativado/role alterado reflete em ≤ vida do refresh).
- **Consent**: implícito para clients first-party confiáveis (AdminPortal) — a tela de consent entra quando houver um client de terceiros real.

## Configuração

| Seção | Uso |
|---|---|
| `SecureGate:Database` | `Provider` (`SqlServer` padrão / `Postgres`, ADR-0018) + `ConnectionString` do banco de plataforma |
| `SecureGate:Tokens:AccessTokenLifetimeMinutes` | Vida do access token (padrão 60) |
| `SecureGate:Signing:CertificatePath/CertificatePassword` | Certificado PKCS#12 — obrigatório em Production |

## Testes

`tests/SecureGate/Secco.SecureGate.Tests` — Testcontainers (ADR-0012): schema ADR-0017 provado por INFORMATION_SCHEMA, fluxo client credentials/JWKS, gestão e catálogo com autorização por scope, cache TTL/stale do client, gestão de roles/usuários, os E2E cross-produto (o LogStream valida token do SecureGate **e resolve tenant pelo catálogo remoto sem nenhum tenant em configuração**, inclusive migrations via `ListAsync`) e o **E2E de login**: authorization code + PKCE ponta a ponta pelo fluxo real do navegador (desafio → login antiforgery → code → troca com `code_verifier` → userinfo → refresh).

## Operador de plataforma (ADR-0023/0024)

O [`Secco.AdminPortal`](../AdminPortal/README.md) consome o SecureGate on-behalf-of um **operador de plataforma** (usuário com o role `platform-operator` num tenant de plataforma). Duas mecânicas sustentam isso: o `/connect/authorize` **filtra o scope `securegate:admin`** — só operadores o recebem (login comum não escala, ADR-0023); e o token do operador é **tenant-less** (escolhe o tenant por requisição via `X-Tenant-Id`) e recebe um **read-set cross-tenant** somente leitura, resolvido no SecureGate para o papel `platform-operator` (ADR-0024).
