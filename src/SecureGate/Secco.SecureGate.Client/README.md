# Secco.SecureGate.Client

Client HTTP do **Secco.SecureGate**, **gerado por NSwag** a partir do [`openapi.json`](../Secco.SecureGate.Api/openapi/openapi.json) versionado (ADR-0006). É a única forma de comunicação entre produtos da plataforma — `HttpClient` manual é proibido.

O código do client é gerado durante o build (`NSwag.ApiDescription.Client`); mudou o contrato → o snapshot é atualizado e o client regenera no mesmo PR. Nunca editar código gerado — ajustes vão em arquivos parciais.

## Além do client gerado

O pacote também entrega, prontas para os produtos consumirem, duas integrações com o SecureGate (fora da pasta gerada):

- **`AddSecureGateTenantCatalog()`** — registra o `SecureGateTenantCatalog : ITenantCatalog` (ADR-0005): resolve as connection strings de tenant a partir do catálogo central do SecureGate, com client credentials automático (scope `catalog:<produto>`), cache com TTL curto e *stale em falha*. Sem a seção `Secco:SecureGate`, o produto segue no catálogo por configuração do SDK (DEV).
- **`AddSecureGatePermissionResolver()`** — registra o `IPermissionResolver` remoto (ADR-0021): resolve `(tenant, role) → permissões` no SecureGate (scope `authorization:read`), consumido pelo `AddSeccoAuthorization()` do SDK.
- **`AddSecureGateAdminClient()`** (pasta `Administration/`) — registra o `ISecureGateClient` tipado completo, autenticado por client credentials com o scope `securegate:admin` (gestão de tenants/roles/usuários), para produtos que precisam chamar a API administrativa do SecureGate diretamente — não só ler o catálogo ou resolver permissões.

Todos usam a mesma seção `Secco:SecureGate` (`BaseUrl`/`ClientId`/`ClientSecret`) e herdam o pipeline de resiliência da plataforma quando o host chama `AddSeccoResilience()`.

## Clients de produto (0.15.0, ADR-0037)

Com escopo `securegate:admin`, registre a credencial `client_credentials` de um sistema da
empresa vinculada a um tenant: `CreateProductClientAsync(tenantId, new ProductClientRequest { Name, Scopes, Roles })`.
O secret volta **só** nessa resposta e em `RotateProductClientSecretAsync` — guarde-o na hora.
O token desse client sai com `tenant_id`, então ele só alcança o próprio tenant. Escopos
aceitos: `logstream`, `notificationhub`. Clients sem tenant (de plataforma) não passam por esta
API: são declarados em `SecureGate:PlatformClients` na configuração do SecureGate.

### Client interativo (ADR-0040)

O mesmo `CreateProductClientAsync` cria um client **interativo** (login de pessoa, authorization code + PKCE, confidencial) vinculado ao tenant: `new ProductClientRequest { Name, Scopes, Type = "authorization_code", RedirectUris, PostLogoutRedirectUris }`. `Type` nulo é `client_credentials` (o padrão).

- `RedirectUris` é obrigatório (1 a 10); `PostLogoutRedirectUris` é opcional (até 10). Absolutas, `http(s)` sem credencial nem fragmento, até 2000 caracteres, sem repetição; `http` só em Development.
- `Roles` não se aplica: a pessoa leva os próprios perfis (informar responde erro de validação). Em `client_credentials`, informar redirect URIs também é erro.
- O tenant de plataforma não aceita client interativo por esta API (409): os clients interativos da plataforma vêm da configuração.
- O token emitido leva o tenant em que a pessoa entrou (o da aplicação, ADR-0040) e só os perfis dela nesse tenant.

## Vínculos de pessoa com tenant (ADR-0040)

Uma pessoa tem a conta em um tenant (o dono) e pode ser **membro** de outros. Com escopo `securegate:admin`:

- `AddTenantMemberAsync(tenantId, new AddTenantMemberRequest { Email })` — inclui no tenant uma pessoa que já tem conta, pelo e-mail; devolve `TenantMemberDto`.
- `SetTenantMemberAsync(tenantId, userId)` — `PUT` idempotente: reativa o vínculo.
- `SuspendTenantMemberAsync(tenantId, userId)` — suspende: a pessoa deixa de receber token do tenant; perfis e histórico ficam.
- `RemoveTenantMemberAsync(tenantId, userId)` — remove o vínculo.

`ListUsersAsync` e `GetUserAsync` passam a trazer `Membership` (`Owner`, `Member` ou `Suspended`) e `OwnerTenantId`; para quem não é dono, os campos de credencial (`HasPassword`, `LocalLoginEnabled`, `TwoFactorEnabled`, `ExternalLogins`) vêm vazios, porque pertencem à conta e ao tenant dono.

## Catálogo de permissões (ADR-0039)

O produto declara suas permissões (`SeccoPermissionDefinition`) e as publica no SecureGate durante o `migrate`:

```csharp
services.AddSecureGatePermissionCatalogPublisher();
services.AddSeccoReferenceSeeder((serviceProvider, cancellationToken) =>
    serviceProvider.GetRequiredService<ISecureGatePermissionCatalogPublisher>()
        .PublishAsync("meu-produto", MeuProdutoPermissions.Catalog, cancellationToken));
```

- O client de plataforma do produto precisa do escopo `permissions:<produto>` (comparado ordinal com o produto da rota).
- Sem a seção `Secco:SecureGate` a publicação é pulada com um log; com a seção, qualquer falha lança.
- Publicar nunca altera perfis: permissão retirada do catálogo continua gravada nos perfis e aparece em `unknownPermissions` do `GetRole` até alguém removê-la (`RemoveUnknownRolePermissions`, por tenant).
