# Arquitetura — visão geral

Panorama digerível da Secco Platform. Para o detalhe e a justificativa de cada decisão, as [ADRs](adr/secco-platform-adrs.md) são a fonte da verdade — nenhum código as contradiz. Para instalar e consumir, veja [getting-started.md](getting-started.md).

## Identidade arquitetural

A plataforma é um **ecossistema de produtos adotáveis de forma independente** via NuGet, que compartilham a mesma identidade:

- **Clean Architecture, 4 camadas** (ADR-0002): `Api → Application → Domain`, `Api → Infrastructure → Application`. Dependências apontam para dentro; zero infraestrutura em Domain/Application.
- **Erros de negócio como valor** (ADR-0004): `Result<T>`/`Error` do SharedKernel — nunca exceções para fluxo. Na borda, viram ProblemDetails.
- **Contrato primeiro** (ADR-0006): cada produto versiona um `openapi.json`; a comunicação entre produtos é **exclusivamente** por clients NSwag gerados (`Secco.<Produto>.Client`) — `HttpClient` manual é proibido. Todo endpoint tem `operationId` estável (`.WithName()`), senão um endpoint novo renomeia métodos de outros clients.
- **Multi-tenancy database-per-tenant** (ADR-0005): nenhuma query cruza tenants; o acesso a dados passa por `ITenantConnectionFactory`.
- **Cross-cutting no SDK** (ADR-0004): correlation, autenticação, tenancy, health, resiliência e autorização vêm do `Secco.SDK`, nunca reimplementados no produto. Background jobs seguem o mesmo espírito: `IBackgroundJobScheduler`/`AddSeccoBackgroundJobs()` (ADR-0015 Camada 2) encapsula o Hangfire com storage SQL Server no banco de **plataforma** do produto (nunca por tenant); o `TenantJobRunner` restaura o tenant no escopo antes do job rodar.
- **Escrita idempotente pelo efeito** (ADR-0034): `PUT`/`DELETE` de fato idempotentes, porque o SDK os repete; `POST` de criação com chave natural responde `409` à duplicata.
- **Estado fora da requisição em três classes** (ADR-0035): cache derivado com TTL, estado compartilhado entre instâncias via `IDistributedCache` e estado durável no banco — nunca memória de processo sozinha para o que várias instâncias precisam ver.
- **Segurança é design, não revisão** (ADR-0020): confiança em input, injeção, vazamento, isolamento de tenant, DoS e dependências são avaliados antes de implementar.
- **Banco em notação húngara por convention** (ADR-0017): `tb_`/`id_pk_`/`ds_`/`dt_`/`fl_`… aplicados pelo EF Core — ninguém digita nome de tabela/coluna. SQL Server é o provider padrão, PostgreSQL o segundo (ADR-0018).

## Os produtos

| Produto | Papel | ADRs-chave |
|---|---|---|
| **Secco.SharedKernel** | Primitivas puras e estáveis (`Result<T>`, paginação, `BaseEntity`, claims, permissions e famílias de permissão) — sem I/O, zero dependências além da BCL | 0003, 0004, 0043 |
| **Secco.SDK.AspNetCore** | Cross-cutting de runtime + composição `AddSeccoPlatform()`, OpenAPI, background jobs | 0004, 0005, 0007, 0015, 0020, 0021 |
| **Secco.SDK.EntityFrameworkCore** | `SeccoDbContext` + nomenclatura de banco + seeding + cifragem de segredo em repouso + migração do tenant no primeiro uso | 0017, 0018, 0019, 0025, 0038 |
| **Secco.SDK.ClientCredentials** | Token de máquina OAuth 2 para os clients de produto (pacote fino, sem Hangfire) | 0037 |
| **Secco.SDK.Logging** | Sink `ILogger` → LogStream (`AddLogStream()`), fila local que nunca bloqueia | 0008 |
| **Secco.SDK.RelyingParty** | Custódia do token da pessoa no servidor para aplicações on-behalf-of, autenticação recente e token trocado | 0046, 0050 |
| **Secco.SDK.Email** | Porta de e-mail + adaptadores SMTP/SendGrid | 0033 |
| **Secco.SDK.Imaging** | Tratamento seguro de imagem enviada por usuário (variantes sem metadados) | 0042 |
| **Secco.SDK.Testing** | Base das factories de teste de integração (SQL Server real, chave aleatória por instância) | 0012, 0027 |
| **Secco.LogStream** | Logging & Observability (produto de referência): logs, processos, chamadas de API, trilha de auditoria síncrona, retenção por classe de dado | 0002, 0008, 0018, 0020 |
| **Secco.SecureGate** | Identity & Access Management (OIDC): emissor único de tokens, catálogo de tenants, Role+Permission, login, federação Entra ID | 0007, 0021, 0022 e 0025–0056 |
| **Secco.AdminPortal** | **O** console de operação da instalação (Blazor Server, relying party OIDC) | 0023, 0024, 0046, 0051 |
| **Secco.NotificationHub** | Notificações multi-canal: e-mail (SMTP/SendGrid) com retry, inbox in-app, Teams e Slack, lote e agendamento | 0002, 0015, 0029 |
| **Secco.Templates** | `dotnet new secco-service` — destila um produto conforme todas as ADRs | 0013 |

## Como uma requisição flui

O `UseSeccoPlatform()` fixa a ordem do pipeline:

```text
correlation → autenticação → tenancy → autorização → endpoint
```

1. **Correlation** (ADR-0008): propaga/gera `X-Correlation-Id` (valida o header recebido antes de reusar).
2. **Autenticação** (ADR-0007): valida o JWT emitido pelo SecureGate via Authority/JWKS. Claims **curtas** (`sub`, `role`, `tenant_id`, `scope`) — sem remapeamento. Token de pessoa leva também `sver`, a versão da sessão: o produto a confere com cache curto e fail-closed, e é isso que faz "encerrar sessões" valer antes de o token expirar (ADR-0032).
3. **Tenancy** (ADR-0005): resolve o tenant — claim `tenant_id` é primária; header `X-Tenant-Id` só sem claim; divergência = 400. O produto lê a connection string do tenant via `ITenantConnectionFactory`, e o banco de um tenant novo é migrado no primeiro uso (ADR-0038).
4. **Autorização** (ADR-0021): o token carrega só `role`; as permissões (`recurso:acao`) são resolvidas em runtime `(tenant, role) → permissões`, com cache de TTL curto e **fail-closed**.

## Multi-tenancy (database-per-tenant)

Cada tenant tem seu próprio banco **por produto**. As connection strings vivem num **catálogo central servido pelo SecureGate** (ADR-0005/0022): o produto consome via `AddSecureGateTenantCatalog()` (do `Secco.SecureGate.Client`) — client credentials automático com scope mínimo, cache com TTL curto e *stale em falha*. Em DEV, sem a configuração, um catálogo por `IConfiguration` do SDK assume.

- **Cifradas em repouso** (ADR-0025): AES-256-GCM na aplicação, formato versionado `secco-enc:v1:`, chave mestra por configuração com fail-fast em Production. A chave entra no backup com o mesmo peso do banco.
- **Provisionadas pela plataforma** (ADR-0028): o SecureGate gera o script de criação do banco e do login do tenant (`db_owner` só naquele banco, nada no servidor) ou o aplica sozinho, se a instalação der a credencial. Sem isso, uma connection string com `sa` alcançaria o banco do vizinho, e o isolamento seria só convenção.
- **Migrations fora do startup** (ADR-0038): o verbo `migrate` do binário aplica migrations e seed de referência antes das réplicas subirem.

O próprio banco do SecureGate guarda dado de todos os tenants, então ele tem isolamento próprio (ADR-0041): escopo de acesso explícito por requisição, filtros globais do EF e **Row-Level Security** nos dois engines, com a credencial de runtime proibida de ser dona do banco.

## Identidade e acesso (SecureGate)

O SecureGate é o **único emissor de tokens** (ADR-0007) e trata identidade como **dado de plataforma** (ADR-0022), sobre **OpenIddict + ASP.NET Identity**; nenhum outro produto conhece o OpenIddict — todos validam JWT padrão.

- **Máquinas** (ADR-0037): client de **produto** vinculado a um tenant, criado pela API, com token que leva `tenant_id`; client de **plataforma** sem tenant, só por configuração (`SecureGate:PlatformClients`).
- **Pessoas**: authorization code + PKCE + refresh. A conta nasce **sem senha**: convite e recuperação por e-mail (ADR-0033), segundo fator TOTP voluntário e obrigatório para o operador. Uma pessoa pode ser membro de vários tenants, e **o tenant do login vem da aplicação** (ADR-0040).
- **Login federado por tenant** (ADR-0026): o tenant pode delegar a autenticação ao próprio Microsoft Entra ID. Federação é **só autenticação**: o SecureGate continua o emissor único e tokens do Entra nunca chegam aos produtos. Por padrão as contas são pré-provisionadas; com o ato explícito do operador, um grupo do diretório passa a decidir **quem tem conta e qual perfil tem** (ADR-0036/0055).
- **Autorização granular** (ADR-0021): Role é o perfil, Permission (`recurso:acao`) é a ação; o mapeamento é por tenant e resolvido em runtime. Cada produto publica o catálogo das próprias permissões, e só permissão de catálogo pode ser acrescentada a um perfil (ADR-0039/0043). Todo tenant tem o perfil implícito `tenant-member`, que toda pessoa ativa recebe sem atribuição (ADR-0056).
- **Sessões revogáveis** (ADR-0032): encerrar sessões, desativar a conta ou retirar um perfil derruba os tokens em uso em até um TTL de cache, sem esperar a expiração.

### Quem administra o quê

Cada instalação é **soberana**: quem opera é a empresa que adotou a plataforma, não a Secco (ADR-0030).

- **Operador da instalação** (`installation-operator` no tenant de plataforma): administra todos os tenants pelo **AdminPortal**, que é *o* console de operação, com ou sem um portal de adotante (ADR-0051). O token do operador é *tenant-less* e recebe um read-set de leitura de logs em qualquer tenant (ADR-0024).
- **Produto-portal** (como a Intranet do adotante): administra **só o próprio tenant**, por um client de máquina com `securegate:tenant-manage`, com trilha síncrona e fail-closed no banco do SecureGate (ADR-0052/0054).
- **Privilégio como ato, não como estado**: ler log cross-tenant a partir de um produto tenant-scoped exige **elevação explícita** por token exchange (RFC 8693), com concessão prévia, TTL curto e auditoria (ADR-0031).
- As capacidades de admin de tenant por concessão, de criação de tenant e de operador designado (ADR-0044, 0047, 0048, 0049, 0050) **valem, mas estão congeladas** pela ADR-0051: não recebem capacidade nova.

## Comunicação entre produtos

Sempre pelos **clients NSwag** gerados a partir do `openapi.json` versionado (ADR-0006). Mudou o contrato → o snapshot e o client são regenerados no mesmo PR (o CI falha em divergência). Todo `HttpClient` registrado herda o pipeline de resiliência da plataforma (ADR-0004). Chamada em nome de uma pessoa leva o token dela (on-behalf-of, custodiado pelo `Secco.SDK.RelyingParty`); chamada de serviço leva token de máquina (`Secco.SDK.ClientCredentials`).

## Onde aprofundar

- **Todas as decisões**: [`adr/secco-platform-adrs.md`](adr/secco-platform-adrs.md) — 56 ADRs Aceitas (cinco congeladas e uma parcialmente substituída pela ADR-0051).
- **Fases e entregáveis**: [`roadmap.md`](roadmap.md).
- **Por produto**: os READMEs em [`src/`](../src).
