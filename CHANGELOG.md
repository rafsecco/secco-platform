# Changelog

Mudanças relevantes dos pacotes publicáveis da Secco Platform.

A ADR-0011 exige **entrada de changelog por pacote** em toda breaking change. Como o repositório é um monorepo, as entradas ficam neste arquivo único, com **uma seção por pacote** — a exigência é que a mudança do pacote esteja registrada, não que exista um arquivo por pacote.

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). Versionamento SemVer estrito por pacote (ADR-0011): a versão sai da tag git via MinVer, nunca de bump manual em csproj.

> **Histórico anterior a 2026-08-30.** Este arquivo nasceu depois das primeiras releases. As versões já publicadas estão listadas com data, que é verificável pela tag git, mas **sem descrição retroativa** — reconstruí-las a posteriori produziria texto plausível e não confiável. O contexto real de cada fase está em [`docs/roadmap.md`](docs/roadmap.md). Entradas descritivas valem a partir daqui.

---

## Não publicado

### Secco.NotificationHub.Client

- **Adicionado** `UpsertChannelConfigurationAsync`, `ListChannelConfigurationsAsync` e `DeleteChannelConfigurationAsync` — gestão do destino dos canais externos por tenant (issue #13, ADR-0029).
- **Alterado (aditivo)** `NotificationDto` ganhou `Channel`, e `Recipient` passou a ser anulável: a entrega deixou de ser sempre de e-mail. O resultado de despacho ganhou `ExternalNotificationIds`.
- Nenhum método existente foi renomeado — segunda validação prática da correção da issue #9.

### Secco.SDK.EntityFrameworkCore

- **Adicionado** `ISeccoSecretCipher` e `AesGcmSecretCipher`: cifragem AES-256-GCM de segredo em repouso, no formato versionado `secco-enc:v1:` da ADR-0025 (ADR-0029).
- Promovidos do `Secco.SecureGate.Infrastructure`, onde eram internos ao produto. O `secco-intranet` já havia reimplementado o mesmo formato por conta própria; o `Secco.NotificationHub` seria a terceira implementação independente do mesmo formato criptográfico. O tipo recebe as chaves já decodificadas e **não** conhece configuração de produto: a política de origem da chave continua em cada produto.
- **Sem mudança de formato nem de dado.** Os testes de comportamento vieram junto e passam com as mesmas asserções, incluindo a do prefixo literal — que é contrato de dado já gravado.

### Secco.NotificationHub.Client

- **Adicionado** `DispatchNotificationBatchAsync` — despacho de um conteúdo para muitos destinos numa chamada só (issue #15).
- Mudança **aditiva**, sem renomear nada. É a primeira validação prática da correção da issue #9: com `operationId` fixo, um endpoint novo entra sem mexer no nome de método de nenhum outro.

---

## Publicado

### Secco.SharedKernel

#### 0.3.4 — 2026-09-05

Patch **sem mudança funcional**: nenhum commit tocou `src/SharedKernel` desde a 0.3.3. A tag existe pelo mesmo motivo da 0.3.3 — o `Secco.SDK.AspNetCore` 0.5.0 e, por transitividade, o `Secco.SDK.Logging` 0.1.0 saem deste commit e dependem do SharedKernel por `ProjectReference`; sem tag estável da dependência no commit empacotado, o MinVer a resolveria como pré-release e o Pack falharia com NU5104 (ADR-0011).

#### 0.3.3 — 2026-08-30

Patch **sem mudança funcional**: o conteúdo é idêntico à 0.3.2 (o diff entre as tags é só a migração de indentação para tab). A tag existe porque o `Secco.SDK.Testing` 0.1.0 sai do mesmo commit e referencia o SharedKernel por `ProjectReference` — sem tag estável da dependência no commit empacotado, o MinVer a resolveria como pré-release e o Pack do dependente falharia com NU5104 (ADR-0011).

| Versão | Data |
|---|---|
| 0.3.2 | 2026-07-19 |
| 0.3.1 | 2026-07-19 |
| 0.3.0 | 2026-07-14 |
| 0.2.0 | 2026-07-12 |
| 0.1.1 | 2026-07-11 |
| 0.1.0 | 2026-07-08 |

### Secco.SDK.AspNetCore

#### 0.5.0 — 2026-09-05

- **Adicionado** `SeccoAmbientContext`: espelho ambiente (`AsyncLocal`) do tenant e da correlação, escrito pelos middlewares de correlação e tenancy e por `TenantScopeExtensions.SetTenant`. Existe porque `ITenantContext`/`ICorrelationContext` são `Scoped` e há consumidores singleton por natureza — o caso concreto é um `ILoggerProvider`. Dentro de um job do Hangfire há escopo de DI mas não há `HttpContext`, então `IHttpContextAccessor` não resolveria o caso geral.
- **Adicionado** `SeccoClientCredentialsHandler` e `SeccoAccessTokenStore` em `Secco.SDK.AspNetCore.Authentication`, promovidos do `Secco.SecureGate.Client`. O mecanismo é OAuth 2 puro contra `/connect/token` — protocolo, não contrato de produto —, então qualquer pacote do SDK que precise de um token de máquina passa a reusá-lo sem depender do pacote de identidade.
- Mudança **aditiva**: nenhuma API existente foi alterada ou removida.

| Versão | Data |
|---|---|
| 0.4.1 | 2026-07-19 |
| 0.4.0 | 2026-07-19 |
| 0.3.0 | 2026-07-14 |
| 0.2.0 | 2026-07-12 |
| 0.1.0 | 2026-07-11 |

### Secco.SDK.EntityFrameworkCore

#### 0.3.0 — 2026-09-05

- **Adicionado** `SeccoDatabaseProviders`: seleção de provider de banco por receita. O produto declara o que aplicar (incluindo o assembly de migrations), o SDK apenas seleciona — **sem nenhuma dependência de engine adicionada ao pacote**, preservando a cláusula de extensibilidade da ADR-0018.
- Mudança **aditiva**. O código estava entregue desde 2026-08-29 e ficou sem publicar: a última tag era de 12/07, então o adotante não tinha como usá-lo.

| Versão | Data |
|---|---|
| 0.2.0 | 2026-07-12 |
| 0.1.0 | 2026-07-11 |

### Secco.LogStream.Client

#### 0.3.0 — 2026-09-05

- **Alterado (quebra)** — todos os métodos passam a ser nomeados pelo `operationId` do endpoint, e não mais derivados do path: `LogEntriesPOSTAsync` → `CreateLogEntryAsync`, `BatchAsync` → `CreateLogEntryBatchAsync`, `LogEntriesGETAsync` → `SearchLogEntriesAsync`, `LogEntriesGET2Async` → `GetLogEntryAsync`, e equivalentes para processos, chamadas de API e auditoria.
- Motivo (issue #9): nome derivado do path **não é estável** — depende de quantos e quais endpoints existem no documento, então adicionar um endpoint renomeia métodos de outros. Foi o que quebrou o `Secco.AdminPortal` na 0.2.0. Esta é a última renomeação: com `operationId` fixo, endpoint novo não mexe mais em método existente.

#### 0.2.0 — 2026-09-05

- **Alterado (quebra fonte)** — `LogEntriesGETAsync` ganhou os parâmetros `serviceName` e `category` **antes** de `page`/`size`. Quem chamava posicionalmente quebra e precisa passar a usar argumentos nomeados. Em `0.x`, minor é o sinal de breaking (ADR-0011). O `Secco.AdminPortal` foi corrigido no mesmo PR e serve de exemplo do ajuste.
- **Adicionado** `CorrelationId`, `ServiceName` e `Category` opcionais em `CreateLogEntryRequest`. A correlação do payload vence o header `X-Correlation-Id` — é o que torna o `/batch` utilizável por um sink que acumula logs de requisições diferentes, que antes recebiam todas a correlação do lote.
- **Adicionado** a superfície de `audit-entries`: `POST` (síncrono, `201`), `GET` por id e busca paginada — a trilha de auditoria da issue #2.
- **Adicionado** `serviceName` e `category` como filtro na busca de `log-entries`.

| Versão | Data |
|---|---|
| 0.1.1 | 2026-07-14 |
| 0.1.0 | 2026-07-12 |

### Secco.SecureGate.Client

#### 0.3.0 — 2026-09-05

- **Adicionado** `ProvisionTenantDatabaseAsync` e `GetTenantDatabaseStatusAsync` — o provisionamento de banco de tenant da ADR-0028 e o painel de estado dos bancos.
- Mudança **aditiva**, sem renomear método existente. Os endpoints do SecureGate usam `.WithName(...)`, então o NSwag gera o nome do método a partir do `operationId` e não do path — nomes ficam estáveis quando um endpoint novo entra. É a diferença que faltou no LogStream, onde a ausência de `.WithName(...)` fez o `LogEntriesGETAsync` renumerar parâmetros na 0.2.0.

| Versão | Data |
|---|---|
| 0.2.1 | 2026-07-19 |
| 0.2.0 | 2026-07-19 |
| 0.1.0 | 2026-07-14 |

### Secco.SDK.Logging

#### 0.1.0 — 2026-09-05

Primeira publicação. Entrega o `AddLogStream()` que a ADR-0008 prometia em 2026-07-04 e que nunca existira — o que havia era o `Secco.LogStream.Client` com `AddLogStreamClient()`, o client HTTP gerado, não o sink de `ILogger`.

- `ILoggerProvider` com fila local limitada (descarte contado, nunca bloqueia o request, nunca lança), lote por tenant e flush no encerramento.
- Enriquecimento automático de tenant, correlação, nome do serviço e categoria.
- Guarda anti-recursão por categoria: sem ela, o envio do lote pelo `HttpClient` geraria logs que provocariam outro envio, num laço que não converge.
- Log sem tenant vai para `Secco:LogStream:PlatformTenantId` quando configurado; sem ele, é descartado com contador — o LogStream é database-per-tenant e sem tenant não há destino (ADR-0005).

### Secco.SDK.Testing

#### 0.1.0 — 2026-08-30

Primeira versão. Base compartilhada das factories de teste de integração (ADR-0027), substituindo cinco cópias divergentes de `*ApiFactory`.

- `SeccoApiFactory<TProgram>` com `ConfigureWebHost` **selado** e extensão por hooks (`ConfigureTestConfiguration`, `ConfigureTestServices`, `OnInitializedAsync`) — selar é o que impede o drift de voltar por herança.
- `Audience` e `MigrateAsync` abstratos: esquecer vira erro de compilação, não teste vermelho no CI.
- Instância de SQL Server isolada por suíte, com override por `SECCO_TEST_SQLSERVER` para máquinas onde N containers saturam o Docker.
- Chave de assinatura **aleatória por instância** — nunca constante embutida (ADR-0020): num pacote publicado, uma chave fixa seria de conhecimento público.
- Nome de database validado por allowlist antes de `CREATE`/`DROP DATABASE`, que não aceitam parametrização.
- Marcado `DevelopmentDependency`: não flui transitivamente para quem referencia o produto.
- Depende de `Secco.SharedKernel` 0.3.3.

### Secco.NotificationHub.Client

#### 0.2.0 — 2026-09-05

- **Alterado (quebra)** — mesma correção: `NotificationsPOSTAsync` → `CreateNotificationAsync`, `NotificationsGETAsync` → `GetNotificationAsync`, e os três de inbox in-app para `GetUnreadInAppNotificationsAsync`, `CountUnreadInAppNotificationsAsync` e `MarkInAppNotificationAsReadAsync`.
- Nenhum consumidor conhecido — o pacote 0.1.0 não é referenciado por nenhum produto nem pelo adotante.

#### 0.1.0 — 2026-08-30

Primeira versão. Client NSwag gerado do `openapi.json` versionado do NotificationHub (ADR-0006) — a única forma legítima de outro produto da plataforma falar com ele. Cobre o despacho multi-canal, a consulta de status e os endpoints de inbox in-app.

### Secco.Templates

| Versão | Data |
|---|---|
| 0.1.0 | 2026-07-14 |
