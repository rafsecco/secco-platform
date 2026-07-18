# Arquitetura — visão geral

Panorama digerível da Secco Platform. Para o detalhe e a justificativa de cada decisão, as [ADRs](adr/secco-platform-adrs.md) são a fonte da verdade — nenhum código as contradiz. Para instalar e consumir, veja [getting-started.md](getting-started.md).

## Identidade arquitetural

A plataforma é um **ecossistema de produtos adotáveis de forma independente** via NuGet, que compartilham a mesma identidade:

- **Clean Architecture, 4 camadas** (ADR-0002): `Api → Application → Domain`, `Api → Infrastructure → Application`. Dependências apontam para dentro; zero infraestrutura em Domain/Application.
- **Erros de negócio como valor** (ADR-0004): `Result<T>`/`Error` do SharedKernel — nunca exceções para fluxo. Na borda, viram ProblemDetails.
- **Contrato primeiro** (ADR-0006): cada produto versiona um `openapi.json`; a comunicação entre produtos é **exclusivamente** por clients NSwag gerados (`Secco.<Produto>.Client`) — `HttpClient` manual é proibido.
- **Multi-tenancy database-per-tenant** (ADR-0005): nenhuma query cruza tenants; o acesso a dados passa por `ITenantConnectionFactory`.
- **Cross-cutting no SDK** (ADR-0004): correlation, autenticação, tenancy, health, resiliência e autorização vêm do `Secco.SDK`, nunca reimplementados no produto.
- **Segurança é design, não revisão** (ADR-0020): confiança em input, injeção, vazamento, isolamento de tenant, DoS e dependências são avaliados antes de implementar.
- **Banco em notação húngara por convention** (ADR-0017): `tb_`/`id_pk_`/`ds_`/`dt_`/`fl_`… aplicados pelo EF Core — ninguém digita nome de tabela/coluna.

## Os produtos

| Produto | Papel | ADRs-chave |
|---|---|---|
| **Secco.SharedKernel** | Primitivas puras e estáveis (`Result<T>`, paginação, `BaseEntity`, claims, permissions) — sem I/O, zero dependências além da BCL | 0003, 0004 |
| **Secco.SDK.AspNetCore** | Cross-cutting de runtime + composição `AddSeccoPlatform()` | 0004, 0005, 0007, 0020, 0021 |
| **Secco.SDK.EntityFrameworkCore** | `SeccoDbContext` + nomenclatura de banco + seeding | 0017, 0018, 0019 |
| **Secco.LogStream** | Logging & Observability (produto de referência): logs, processos, chamadas de API, retenção | 0002, 0018 |
| **Secco.SecureGate** | Identity & Access Management (OIDC): emissor de tokens, catálogo de tenants, Role+Permission, login | 0007, 0021, 0022, 0023, 0024 |
| **Secco.AdminPortal** | Console de operação (Blazor Server, relying party OIDC) | 0023, 0024 |
| **Secco.NotificationHub** | Notificações multi-canal: e-mail assíncrono com retry + inbox in-app | 0002, 0015 |
| **Secco.Templates** | `dotnet new secco-service` — destila um produto conforme todas as ADRs | 0013 |

## Como uma requisição flui

O `UseSeccoPlatform()` fixa a ordem do pipeline:

```
correlation → autenticação → tenancy → autorização → endpoint
```

1. **Correlation** (ADR-0008): propaga/gera `X-Correlation-Id` (valida o header recebido antes de reusar).
2. **Autenticação** (ADR-0007): valida o JWT emitido pelo SecureGate via Authority/JWKS. Claims **curtas** (`sub`, `role`, `tenant_id`, `scope`) — sem remapeamento.
3. **Tenancy** (ADR-0005): resolve o tenant — claim `tenant_id` é primária; header `X-Tenant-Id` só sem claim; divergência = 400. O produto lê a connection string do tenant via `ITenantConnectionFactory`.
4. **Autorização** (ADR-0021): o token carrega só `role`; as permissões (`recurso:acao`) são resolvidas em runtime `(tenant, role) → permissões`, com cache de TTL curto e **fail-closed**.

## Multi-tenancy (database-per-tenant)

Cada tenant tem seu próprio banco **por produto**. As connection strings vivem num **catálogo central servido pelo SecureGate** (ADR-0005/0022): o produto consome via `AddSecureGateTenantCatalog()` (do `Secco.SecureGate.Client`) — client credentials automático com scope mínimo, cache com TTL curto e *stale em falha*. Em DEV, sem a configuração, um catálogo por `IConfiguration` do SDK assume.

## Identidade e acesso (SecureGate)

O SecureGate é o **único emissor de tokens** (ADR-0007) e trata identidade como **dado de plataforma** (ADR-0022): um banco próprio com usuários (por tenant), roles/permissões (por tenant), clients OIDC e o catálogo de tenants. Baseado em **OpenIddict + ASP.NET Identity**; nenhum outro produto conhece o OpenIddict — todos validam JWT padrão.

- **Máquinas**: client credentials (clients OIDC com roles).
- **Usuários**: authorization code + PKCE + refresh, com telas de login.
- **Autorização granular** (ADR-0021): Role é o perfil, Permission (`recurso:acao`) é a ação; o mapeamento é por tenant e resolvido em runtime.
- **Operador de plataforma** (ADR-0023/0024): o AdminPortal age **on-behalf-of** um operador cross-tenant; o token do operador é *tenant-less* e recebe um read-set cross-tenant para inspeção (somente leitura).

## Comunicação entre produtos

Sempre pelos **clients NSwag** gerados a partir do `openapi.json` versionado (ADR-0006). Mudou o contrato → o snapshot e o client são regenerados no mesmo PR (o CI falha em divergência). Todo `HttpClient` registrado herda o pipeline de resiliência da plataforma (ADR-0004).

## Onde aprofundar

- **Todas as decisões**: [`adr/secco-platform-adrs.md`](adr/secco-platform-adrs.md) — 24 ADRs Aceitas.
- **Fases e entregáveis**: [`roadmap.md`](roadmap.md).
- **Por produto**: os READMEs em [`src/`](../src).
