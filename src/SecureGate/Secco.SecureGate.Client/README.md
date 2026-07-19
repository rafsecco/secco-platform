# Secco.SecureGate.Client

Client HTTP do **Secco.SecureGate**, **gerado por NSwag** a partir do [`openapi.json`](../Secco.SecureGate.Api/openapi/openapi.json) versionado (ADR-0006). É a única forma de comunicação entre produtos da plataforma — `HttpClient` manual é proibido.

O código do client é gerado durante o build (`NSwag.ApiDescription.Client`); mudou o contrato → o snapshot é atualizado e o client regenera no mesmo PR. Nunca editar código gerado — ajustes vão em arquivos parciais.

## Além do client gerado

O pacote também entrega, prontas para os produtos consumirem, duas integrações com o SecureGate (fora da pasta gerada):

- **`AddSecureGateTenantCatalog()`** — registra o `SecureGateTenantCatalog : ITenantCatalog` (ADR-0005): resolve as connection strings de tenant a partir do catálogo central do SecureGate, com client credentials automático (scope `catalog:<produto>`), cache com TTL curto e *stale em falha*. Sem a seção `Secco:SecureGate`, o produto segue no catálogo por configuração do SDK (DEV).
- **`AddSecureGatePermissionResolver()`** — registra o `IPermissionResolver` remoto (ADR-0021): resolve `(tenant, role) → permissões` no SecureGate (scope `authorization:read`), consumido pelo `AddSeccoAuthorization()` do SDK.
- **`AddSecureGateAdminClient()`** (pasta `Administration/`) — registra o `ISecureGateClient` tipado completo, autenticado por client credentials com o scope `securegate:admin` (gestão de tenants/roles/usuários), para produtos que precisam chamar a API administrativa do SecureGate diretamente — não só ler o catálogo ou resolver permissões.

Todos usam a mesma seção `Secco:SecureGate` (`BaseUrl`/`ClientId`/`ClientSecret`) e herdam o pipeline de resiliência da plataforma quando o host chama `AddSeccoResilience()`.
