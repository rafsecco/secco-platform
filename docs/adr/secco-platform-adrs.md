# Secco Platform — Architecture Decision Records (ADR)

> Fonte da verdade arquitetural do ecossistema Secco Platform.
> Nenhum código deve contradizer uma ADR com status **Aceita**.
> Para mudar uma decisão, cria-se uma nova ADR que **substitui** a anterior — ADRs nunca são editadas retroativamente nem apagadas.

**Última atualização:** 2026-10-22 (ADR-0054 aceita)
**Produtos cobertos:** Secco.SecureGate, Secco.LogStream, Secco.NotificationHub, Secco.Configuration, Secco.FeatureFlags, Secco.Audit, Secco.AdminPortal, Secco.SharedKernel, Secco.SDK, Secco.Templates

---

## Como usar este documento

1. Toda decisão arquitetural relevante (que afete mais de um produto, ou que seja difícil de reverter) vira uma ADR.
2. Status possíveis: `Proposta` → `Aceita` → `Substituída por ADR-XXXX` ou `Rejeitada`.
3. Ao iniciar qualquer trabalho em um projeto Secco.*, consultar as ADRs aplicáveis antes de codificar.
4. ADRs curtas são melhores que ADRs completas. Contexto, decisão, consequências. Nada mais.

### Template

```markdown
## ADR-XXXX: Título da decisão

**Status:** Proposta | Aceita | Substituída por ADR-YYYY | Rejeitada
**Data:** AAAA-MM-DD

### Contexto
Qual problema estamos resolvendo e quais forças estão em jogo.

### Decisão
O que foi decidido, em voz ativa: "Usaremos X porque Y."

### Consequências
O que fica mais fácil, o que fica mais difícil, o que passa a ser proibido.
```

---

## ADR-0001: Monorepo com pacotes NuGet de adoção independente

**Status:** Aceita
**Data:** 2026-07-05

### Contexto
A plataforma tem múltiplos produtos (SecureGate, LogStream, etc.) que compartilham `Secco.SharedKernel` e `Secco.SDK`, ambos em evolução rápida. Time pequeno. Multi-repo exigiria publicar pacote e atualizar N repositórios a cada mudança no kernel, criando atrito e divergência de versões. Ao mesmo tempo, é requisito que uma empresa possa adotar apenas um produto isoladamente.

### Decisão
Um único repositório `secco-platform` contendo todos os produtos. A independência de adoção é garantida por **artefatos** (pacotes NuGet e deployables independentes), não por repositórios. Extração para repositório próprio só ocorrerá se um produto for aberto como open source com contribuidores externos, via nova ADR.

Estrutura raiz:

```
secco-platform/
├── Directory.Build.props     # propriedades comuns de build (a raiz é obrigatória:
├── Directory.Packages.props  # o MSBuild os descobre subindo a árvore a partir de cada projeto)
├── nuget.config
├── .editorconfig
├── docs/
│   ├── adr/
│   └── roadmap.md
├── src/
│   ├── SharedKernel/
│   ├── SDK/
│   ├── LogStream/
│   │   ├── Secco.LogStream.Api/
│   │   ├── Secco.LogStream.Application/
│   │   ├── Secco.LogStream.Domain/
│   │   ├── Secco.LogStream.Infrastructure/
│   │   └── Secco.LogStream.Client/        # gerado por NSwag
│   ├── SecureGate/
│   └── AdminPortal/
├── templates/                # Secco.Templates (dotnet new)
├── tests/
│   ├── LogStream/
│   └── SecureGate/
└── Secco.Platform.slnx          # formato XML do .NET 10; + solution filters (.slnf) por produto
```

### Consequências
- Refatorações no kernel são commits atômicos que atualizam todos os consumidores.
- Um único conjunto de convenções de build, CI, analyzers e formatação.
- CI usa *path filters* para buildar/publicar apenas o que mudou.
- Solution filters (`Secco.LogStream.slnf`) mantêm a experiência de IDE leve por produto.

---

## ADR-0002: Clean Architecture com layout padrão por produto

**Status:** Aceita
**Data:** 2026-07-04

### Contexto
Todos os produtos devem ter a mesma identidade técnica; um desenvolvedor que conhece um produto deve navegar qualquer outro sem reaprender a estrutura.

### Decisão
Todo produto segue quatro camadas com direção de dependência estrita para dentro:

```
Api → Application → Domain
Api → Infrastructure → Application → Domain
```

- **Domain:** entidades, value objects, regras de negócio, eventos de domínio. Sem dependências externas (apenas Secco.SharedKernel).
- **Application:** casos de uso, interfaces de portas (repositórios, serviços externos), validação. Retorna `Result<T>`.
- **Infrastructure:** EF Core, provedores externos, implementações de portas.
- **Api:** endpoints, autenticação, composição de DI, OpenAPI.

### Consequências
- Domain e Application são testáveis sem infraestrutura.
- Proibido: referência de Domain/Application a pacotes de infraestrutura (EF, HTTP, etc.).
- O template `Secco.Templates` materializa este layout (ADR-0013).

---

## ADR-0003: Escopo e regras de admissão do Secco.SharedKernel

**Status:** Aceita
**Data:** 2026-07-05

### Contexto
Shared kernels tendem a virar depósito de código genérico, acoplando todos os produtos a um pacote instável. Esse é o maior risco estrutural da plataforma.

### Decisão
`Secco.SharedKernel` contém **apenas primitivas estáveis e puras**:

`Result<T>`, `Error`, `PagedResult<T>`, `PageRequest`, `ApiResponse<T>`, `BaseEntity`, `AuditableEntity`, exceções base, contratos e constantes compartilhados, extensões de BCL.

Regras de admissão (todas obrigatórias):
1. Usado por **dois ou mais** produtos (ou pelo SDK).
2. **Zero dependências** além da BCL.
3. Sem I/O, sem lógica de infraestrutura, sem estado.
4. Interface estável — mudança prevista? Não entra.

O que **não** entra: resolução de tenant, middlewares, HTTP, autenticação, logging — isso é `Secco.SDK` (ADR-0004).

### Consequências
- Breaking change no kernel exige major version e justificativa em ADR.
- Em caso de dúvida, o código fica no produto. Promover ao kernel depois é barato; rebaixar é caro.

---

## ADR-0004: Secco.SDK — comportamento transversal de runtime

**Status:** Aceita
**Data:** 2026-07-04

### Contexto
Todas as APIs da plataforma devem se comportar de forma idêntica em autenticação, correlação, tenancy, resiliência e health checks.

### Decisão
`Secco.SDK` (pacote `Secco.SDK.AspNetCore`) fornece extensões de composição:

```csharp
builder.Services.AddSeccoPlatform(options => { ... });
// que agrega:
// AddSeccoAuthentication()  — JWT/OIDC via SecureGate (ADR-0007)
// AddSeccoCorrelation()     — X-Correlation-Id propagado em toda a cadeia
// AddSeccoTenancy()         — resolução de tenant (ADR-0005)
// AddSeccoResilience()      — políticas de retry/timeout padrão (Polly)
// AddSeccoHealthChecks()    — /health/live e /health/ready padronizados
```

### Consequências
- Nenhum produto implementa esses cross-cutting concerns localmente.
- O SDK depende do SharedKernel; nunca o contrário.
- O SDK pode depender de pacotes externos (Polly, OpenTelemetry) — por isso é separado do kernel.

---

## ADR-0005: Multi-tenancy — Database per Tenant

**Status:** Aceita
**Data:** 2026-07-04

### Contexto
Isolamento forte de dados entre clientes corporativos, simplicidade de backup/restore por cliente e conformidade (LGPD) favorecem isolamento físico.

### Decisão
Cada tenant possui banco de dados próprio. O `Secco.SDK` fornece:
- Resolução de tenant por claim do token (primário) ou header `X-Tenant-Id` (cenários internos).
- `ITenantConnectionFactory` que resolve a connection string do tenant a partir de um catálogo central.
- Migrations aplicadas por tenant via processo controlado (não no startup em produção).

### Consequências
- Proibido: qualquer query que cruze dados de tenants distintos.
- Custo operacional maior (N bancos) — aceito em troca do isolamento.
- O catálogo de tenants é dado de plataforma, gerenciado pelo AdminPortal.

---

## ADR-0006: Contratos HTTP — OpenAPI + NSwag + Scalar; clients sempre gerados

**Status:** Aceita
**Data:** 2026-07-04

### Contexto
Comunicação entre produtos (ex.: SecureGate → LogStream) e entre consumidores externos e a plataforma deve ter contrato explícito e zero código HTTP manual.

### Decisão
1. Toda API expõe OpenAPI gerado no build; Scalar como UI de documentação.
2. Para cada API existe um projeto `Secco.<Produto>.Client` **gerado por NSwag** a partir do `openapi.json`, empacotado como NuGet.
3. Comunicação entre produtos Secco usa exclusivamente esses clients:

```csharp
builder.Services.AddLogStream(o => o.BaseUrl = ...);
// internamente: client NSwag + resiliência do SDK + propagação de correlation/tenant
```

4. O `openapi.json` de cada API é versionado no repositório; o CI compara o gerado com o versionado e **falha em breaking change não declarado**.

### Consequências
- Proibido: `HttpClient` manual entre serviços Secco.
- Mudou contrato → regenerar client no mesmo PR.
- O snapshot do OpenAPI vira ferramenta de detecção de breaking changes.

---

## ADR-0007: Autenticação e autorização — JWT + OIDC via Secco.SecureGate

**Status:** Aceita
**Data:** 2026-07-04 (claims detalhados em 2026-07-09)

### Contexto
Identidade deve ser um serviço da plataforma, não uma preocupação de cada produto. Como o Secco.SecureGate é emissor próprio, a plataforma não herda convenção de claims de terceiros — precisa fixar uma. O `JwtSecurityTokenHandler` do ASP.NET Core remapeia, por padrão e silenciosamente, claims curtos do JWT (`sub`, `role`) para URIs longas de `System.Security.Claims.ClaimTypes`. Deixar esse comportamento implícito é fonte de bug de autorização: código que busca `User.FindFirst("role")` falha silenciosamente (retorna null) se o mapeamento automático estiver ativo, pois o claim real passa a ter outro nome.

### Decisão
- Secco.SecureGate é o único emissor de tokens (OIDC provider) da plataforma.
- Todas as APIs validam JWT via `AddSeccoAuthentication()` do SDK (authority = SecureGate, validação por JWKS).
- **Claims curtos, padrão JWT/OIDC — nunca `System.Security.Claims.ClaimTypes` (URIs longas):**

  | Claim | Significado |
  |---|---|
  | `sub` | id do usuário (subject) |
  | `role` | role(s) do usuário |
  | `tenant_id` | tenant ao qual o token pertence (ADR-0005) |
  | `scope` | escopos concedidos (client credentials, service-a-service) |

- `AddSeccoAuthentication()` desliga o mapeamento automático de entrada centralmente (`JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear()` ou equivalente em `Microsoft.IdentityModel`) e configura `TokenValidationParameters.RoleClaimType = "role"` e `NameClaimType = "sub"` — nenhum produto decide isso individualmente.
- Autorização por policies nomeadas em constantes no SharedKernel, construídas sobre esses claims. Autorização granular por ação (não só role) segue a ADR-0021.
- Comunicação serviço-a-serviço usa client credentials flow (token carrega `scope`, sem `sub` de usuário).

### Consequências
- Nenhum produto armazena credenciais de usuários.
- SecureGate é dependência de runtime de todos — exige SLA e HA superiores aos demais.
- `User.FindFirst("sub")` / `User.IsInRole(...)` funcionam com os nomes exatamente como emitidos pelo token — sem tradução mental nem mapeamento surpresa.
- Login federado futuro (Entra ID, OIDC de terceiros) já parte de uma convenção compatível com claims curtos padrão, sem remapeamento adicional.
- Todo produto que usa `[Authorize]` herda a configuração via `AddSeccoAuthentication()`; implementar autenticação fora dessa extensão é proibido (ADR-0020 — autenticação/autorização sempre explícitas, nunca reimplementadas ponto a ponto).

---

## ADR-0008: Logging e observabilidade — Secco.LogStream via client + OpenTelemetry

**Status:** Aceita
**Data:** 2026-07-04

### Contexto
Produtos não devem implementar persistência de logs; o LogStream é o destino central. Ao mesmo tempo, precisamos de traces e métricas com padrão de mercado.

### Decisão
- Produtos usam `ILogger<T>` normalmente; um provider do SDK (`AddLogStream()`) envia os logs ao Secco.LogStream via `Secco.LogStream.Client` (batch + fila local + retry — nunca bloqueia o request).
- Correlation id, tenant id e nome do serviço são enriquecidos automaticamente pelo SDK.
- Traces e métricas via OpenTelemetry, com exportador configurável.
- Logging estruturado obrigatório (message templates, nunca interpolação).

### Consequências
- Proibido: SecureGate (ou qualquer produto) gravar logs em banco próprio.
- LogStream indisponível não pode derrubar produtos: fila local com descarte controlado.

---

## ADR-0009: Versionamento de APIs

**Status:** Aceita
**Data:** 2026-07-04

### Decisão
- Versionamento por URL: `/api/v1/...`.
- Breaking change → nova versão major da API; a anterior permanece por período de deprecação documentado.
- Mudanças aditivas (novos campos opcionais, novos endpoints) não geram nova versão.
- A versão da API é independente da versão do pacote client (SemVer próprio, ADR-0011).

---

## ADR-0010: Convenções de nomenclatura

**Status:** Aceita
**Data:** 2026-07-04

### Decisão

| Elemento | Convenção | Exemplo |
|---|---|---|
| Projetos | `Secco.<Produto>.<Camada>` | `Secco.LogStream.Application` |
| Pacotes NuGet | idem projeto | `Secco.LogStream.Client` |
| Namespaces | espelham o projeto | `Secco.LogStream.Domain.Entities` |
| Endpoints | kebab-case, plural | `/api/v1/log-entries` |
| JSON | camelCase | `correlationId` |
| Tabelas/colunas | notação húngara (ADR-0017) | `tb_log_entries.id_fk_tenant` |
| Casos de uso | verbo + substantivo | `CreateLogEntryHandler` |
| Testes | `Metodo_Cenario_Resultado` | `Create_WhenTenantMissing_ReturnsFailure` |
| Branches | `feature/`, `fix/`, `chore/` | `feature/logstream-retention` |
| Commits | Conventional Commits | `feat(logstream): add retention policy` |

---

## ADR-0011: Pacotes NuGet, versionamento e feed

**Status:** Aceita
**Data:** 2026-07-05

### Decisão
- Pacotes publicáveis: `Secco.SharedKernel`, `Secco.SDK.AspNetCore`, `Secco.<Produto>.Client`.
- SemVer estrito por pacote, com versionamento automático via **MinVer** a partir de tags git com prefixo (`sharedkernel/v1.2.0`, `logstream-client/v0.3.0`).
- **Central Package Management** (`Directory.Packages.props`) para todas as dependências do monorepo.
- Feed inicial: **GitHub Packages** (privado). Migração para nuget.org quando/se a plataforma for pública, via nova ADR.

### Consequências
- Publicar = criar tag. Sem bump manual de versão em csproj.
- Breaking change em pacote exige major + entrada no CHANGELOG do pacote.

---

## ADR-0012: Estratégia de testes

**Status:** Aceita
**Data:** 2026-07-04

### Decisão
- **Unit:** Domain e Application, sem infraestrutura. Maior volume.
- **Integration:** Infrastructure + Api com **Testcontainers** (SQL Server como padrão conforme ADR-0018; PostgreSQL na matriz quando suportado; Redis real) e `WebApplicationFactory`.
- **Contract:** o `openapi.json` versionado é o teste de contrato (ADR-0006); breaking change não declarado falha o CI.
- xUnit + FluentAssertions + NSubstitute em toda a plataforma.
- Gate de CI: testes verdes obrigatórios; cobertura é métrica observada, não gate.

---

## ADR-0013: Secco.Templates — o padrão executável

**Status:** Aceita
**Data:** 2026-07-04

### Decisão
Um template `dotnet new secco-service` gera um produto completo já conforme todas as ADRs: quatro camadas, SDK plugado, OpenAPI + Scalar, client NSwag configurado, testes de exemplo, Dockerfile e pipeline. Novo produto na plataforma **nasce do template**, nunca de cópia manual.

### Consequências
- O template é atualizado sempre que uma ADR muda o padrão.
- Divergência entre template e ADRs é bug de prioridade alta.

---

## ADR-0014: CI/CD

**Status:** Aceita
**Data:** 2026-07-05

### Decisão
- GitHub Actions com **path filters**: mudança em `src/LogStream/**` builda apenas LogStream (+ dependentes de kernel/SDK quando estes mudam).
- Pipeline por produto: build → testes → geração/validação de OpenAPI → pack de client.
- Publicação de NuGet disparada por tag (ADR-0011); deploy de serviço disparado por tag `logstream/v*`.
- Artefatos de deploy: imagens de contêiner.

---

## ADR-0015: Background processing

**Status:** Aceita
**Data:** 2026-07-05

### Contexto
Produtos precisam de trabalho assíncrono com naturezas distintas: manutenção periódica in-process (retenção de logs), trabalho persistente com retry e visibilidade (envios em lote), e — futuramente — comunicação assíncrona entre produtos. Uma única ferramenta para os três casos ou é insuficiente ou é excesso de infraestrutura, quebrando a promessa de adoção leve de produtos isolados.

### Decisão
Estratégia em três camadas, com critérios objetivos de escalada:

**Camada 1 — Nativo (`BackgroundService` + `PeriodicTimer`):** para manutenção periódica in-process onde perder uma execução por restart é aceitável e não há necessidade de retry, distribuição ou visibilidade. Ex.: purge de retenção, limpeza de caches.

**Camada 2 — Hangfire com storage SQL Server (padrão da plataforma):** obrigatória quando qualquer um destes critérios surgir: persistência de jobs entre restarts, retry automático, agendamento gerenciável, ou visibilidade operacional (dashboard). O storage em SQL Server (ADR-0018) não adiciona infraestrutura nova. Regras:
- Produtos **nunca acoplam ao Hangfire diretamente**: usam a abstração `IBackgroundJobScheduler` do `Secco.SDK`, permitindo troca de implementação pelo adotante.
- Multi-tenancy: jobs vivem no **banco de catálogo da plataforma** (não por tenant); o `tenant_id` viaja no payload e o SDK restaura o contexto de tenant na execução (ADR-0005).
- Uso restrito ao núcleo gratuito (LGPL); recursos da versão Pro (batches etc.) exigem nova avaliação nesta ADR antes de qualquer adoção.

**Camada 3 — Mensageria com broker: adiada por ADR futura.** Será aberta quando surgir o primeiro caso real de comunicação assíncrona entre produtos (provavelmente no NotificationHub). Candidatos registrados: CAP, Wolverine, MassTransit v8 — com a ressalva de que o MassTransit v9 passa a ser comercial (mesma armadilha de licença do FluentAssertions v8; avaliar antes de adotar).

### Consequências
- Produtos isolados permanecem leves: quem não precisa de jobs persistentes não carrega Hangfire.
- O critério de escalada é objetivo — elimina a discussão "nativo ou Hangfire?" caso a caso.
- A abstração no SDK evita lock-in, ao custo de manter uma interface própria sobre o Hangfire.
- Descartado de ofício: soluções cloud-native (Azure Functions etc.) — amarrariam a plataforma a um provedor, incompatível com adotantes self-hosted.

---

## ADR-0016: Prefixo e marca — Secco.*

**Status:** Aceita
**Data:** 2026-07-04

### Contexto
O prefixo original `RS.*` derivava das iniciais do autor (Rafael Secco). Prefixos de duas letras identificam mal o dono, têm alto risco de colisão no nuget.org e são um caso fraco para o programa de *package ID prefix reservation*, cujo critério central é o prefixo identificar claramente o proprietário. A troca precisa ocorrer antes do nascimento de novos produtos, enquanto o custo de renomeação é baixo.

### Decisão
- Prefixo oficial da plataforma: **`Secco.*`** — em projetos, namespaces, pacotes NuGet e nomes de assembly.
- Repositório: `secco-platform`. Marca pública: **Secco Platform**.
- Extensões do SDK seguem o padrão `AddSecco*()`.
- Antes da primeira publicação pública, verificar colisões com `id:Secco` na busca do nuget.org e solicitar a **reserva do prefixo `Secco.`** para a conta do autor.
- O projeto existente (RS.Logging/RS.LogStream) é renomeado para `Secco.LogStream.*` no momento da migração para o monorepo (ADR-0001).
- Projetos consumidores fora da plataforma (ex.: RS.Agenda, RS.Payment do produto Slotly) decidem sua própria nomenclatura; não são obrigados a adotar o prefixo.

### Consequências
- Nenhum código novo usa o prefixo `RS.`.
- Identidade única de pessoa → marca, sem renomeação futura.
- Documentação, templates e skills de desenvolvimento referenciam exclusivamente `Secco.*`.

---

## ADR-0017: Nomenclatura de banco de dados — notação húngara

**Status:** Aceita
**Data:** 2026-07-05

### Contexto
A ADR-0010 definia snake_case simples para tabelas e colunas. Adota-se notação húngara com prefixos semânticos, tornando o tipo e o papel de cada coluna evidentes em qualquer query, sem consultar o schema. Todos os prefixos são minúsculos: é o único formato com comportamento idêntico nos engines suportados (ADR-0018) — o SQL Server preserva a caixa, mas o PostgreSQL converte identificadores não-citados para minúsculas, e prefixos maiúsculos (`TB_`) exigiriam aspas duplas em todo SQL.

### Decisão

**Prefixos de coluna** (prefixo + snake_case do nome):

| Prefixo | Semântica | Exemplo |
|---|---|---|
| `id_pk_` | chave primária | `id_pk_log_entry` |
| `id_fk_` | chave estrangeira | `id_fk_tenant` |
| `id_pfk_` | membro de PK composta que também é FK (tabelas associativas) | `id_pfk_user` |
| `ds_` | texto/descrição | `ds_message`, `ds_email` |
| `dt_` | data/hora | `dt_created_at`, `dt_expires` |
| `nr_` | número/métrica | `nr_attempts`, `nr_duration_ms` |
| `ie_` | enum/indicador | `ie_log_level`, `ie_status` |
| `fl_` | flag/booleano | `fl_active`, `fl_deleted` |
| `vl_` | valor monetário | `vl_price`, `vl_total` |
| `qt_` | quantidade | `qt_items`, `qt_retries` |

**Prefixos de objeto:**

| Prefixo | Objeto | Exemplo |
|---|---|---|
| `tb_` | tabela | `tb_log_entries` |
| `vw_` | view | `vw_active_tenants` |
| `pk_` / `fk_` / `uk_` | constraints (primária, estrangeira, única) | `fk_log_entries_tenant` |
| `idx_` / `ft_` | índice comum / full-text | `idx_log_entries_dt_created_at` |
| `sp_` / `fn_` | procedure / function — padrão `<verbo>_<nome>` | `sp_purge_old_logs`, `fn_select_active_tenants` |

Regras complementares:
- PK: `id_pk_<entidade no singular>`; FK: `id_fk_<tabela referenciada no singular>`; coluna que é membro de PK composta **e** FK: `id_pfk_<referenciada no singular>`.
- Consequência assumida: a mesma coluna lógica tem nome distinto em cada lado do relacionamento (`id_pk_tenant` na origem, `id_fk_tenant` em quem referencia) — é intencional: os JOINs explicitam a direção (`ON le.id_fk_tenant = t.id_pk_tenant`).
- Booleanos descartam o prefixo `Is/Has` do C#: `IsActive` → `fl_active`.
- Constraints e índices: `<prefixo>_<tabela sem tb_>_<colunas>`.
- Procedures e functions: `sp_`/`fn_` + **verbo** + objeto. Verbos CRUD padronizados: `select`, `get` (leitura pontual), `insert`, `update`, `delete`, `upsert`; operações de negócio usam verbo descritivo livre (`purge`, `rebuild`, `merge`). Convenção semântica da plataforma, válida em qualquer engine: `fn_*` retorna dados (`fn_select_*`, `fn_get_*`); `sp_*` concentra mutações/batch (no PostgreSQL isso coincide com a natureza de functions vs procedures; no SQL Server é disciplina nossa).
- A tradução C# → banco é feita por **convention global do EF Core** no SDK (deriva prefixo do tipo CLR e do papel na chave); mapeamento manual de nome de coluna só em exceções, via `[Column]` explícito.
- Ambiguidade `nr_`/`vl_`/`qt_` em decimais: `decimal` → `vl_` por padrão; quantidades e métricas usam override explícito.

### Consequências
- Qualquer query revela tipo e papel das colunas sem consultar o schema.
- Nenhum dev nomeia colunas manualmente: a convention garante o padrão; migrations geradas já saem corretas.
- Onboarding exige aprender a tabela de prefixos (mitigado pela skill `secco-db-naming`).
- Substitui parcialmente a ADR-0010 (linha de tabelas/colunas).

---

## ADR-0018: Providers de banco de dados — SQL Server como padrão

**Status:** Aceita
**Data:** 2026-07-05

### Contexto
A plataforma pertence ao ecossistema Microsoft (.NET), e seu público corporativo primário opera majoritariamente sobre SQL Server. Ao mesmo tempo, produtos da plataforma já nasceram sobre PostgreSQL e a adoção independente exige flexibilidade de engine.

### Decisão
- **SQL Server é o provider padrão** de todos os produtos da plataforma: é o engine dos templates, exemplos, documentação e da configuração default.
- **PostgreSQL é o segundo provider suportado**, com paridade de testes.
- A arquitetura permanece **extensível a outros engines**: nenhum código de Domain/Application conhece o provider; acesso a dados via EF Core com abstrações do SDK.
- Migrations são geradas **por provider** (assemblies de migration separados por engine, quando um produto suportar mais de um).
- SQL cru (views, functions, procedures) é mantido por provider, no mesmo padrão de nomenclatura (ADR-0017); recursos exclusivos de um engine só entram com fallback ou feature-gate documentado.
- Testes de integração rodam contra o provider padrão via Testcontainers (`Testcontainers.MsSql`); produtos que suportam PostgreSQL adicionam a matriz correspondente.

### Consequências
- Templates e `Secco.Templates` nascem configurados para SQL Server; trocar de provider é decisão explícita do adotante.
- A nomenclatura minúscula da ADR-0017 é reforçada: é o único formato com comportamento idêntico em SQL Server (preserva caixa) e PostgreSQL (dobra para minúsculas).
- Manter dois providers custa: cada feature com SQL cru é escrita e testada duas vezes. Aceito em troca do alcance de adoção.
- Prefixo `sp_` **mantido** (decisão de 2026-07-05): o custo de lookup no banco `master` do SQL Server (prefixo reservado a procedures de sistema) é conhecido e aceito conscientemente em favor da consistência da notação (ADR-0017).

---

## ADR-0019: Seed de dados — referência vs desenvolvimento

**Status:** Aceita
**Data:** 2026-07-05

### Contexto
Aplicações precisam de dois tipos de dados iniciais com naturezas opostas: dados **obrigatórios** para o sistema funcionar (ex.: valores default de enums dinâmicos cadastrados em banco, registros de sistema, configurações padrão) e dados **de amostra** para desenvolver e navegar na aplicação sem cadastro manual. Misturá-los causa o vazamento clássico de dados fake em produção.

### Decisão
Duas categorias de seed, com contratos distintos:

**1. Seed de referência — roda em TODOS os ambientes**
- Conteúdo: valores obrigatórios/default de enums dinâmicos, registros de sistema, configurações padrão.
- **Idempotente** (upsert por chave natural/determinística; IDs determinísticos) — reexecutar nunca duplica nem corrompe.
- Versionado junto do schema: integra o pipeline de **provisionamento de cada tenant** (ADR-0005) e reexecuta após cada migration, em todos os bancos de tenant.
- Mudança em seed de referência é revisada com o mesmo rigor de uma migration.

**2. Seed de desenvolvimento — roda APENAS em DEV**
- Conteúdo: tenants de exemplo, usuários, registros de domínio suficientes para navegar na aplicação imediatamente após subir o ambiente.
- **Guarda dupla obrigatória:** `IHostEnvironment.IsDevelopment()` **e** flag explícita de configuração (`Secco:Seed:Development = true`). Sem as duas, não executa.
- Executa sempre **após** o seed de referência (constrói sobre ele).
- Dados realistas gerados com **Bogus** (locale `pt_BR`), com seed randômico fixo para reprodutibilidade.

Organização por produto: `Infrastructure/Seeding/` com `ReferenceDataSeeder` e `DevelopmentDataSeeder`; orquestração exposta pelo SDK e incluída no template (ADR-0013).

### Consequências
- Ambiente dev sobe navegável, sem cadastro manual; enums dinâmicos jamais chegam vazios a produção.
- Seeds são artefatos de código revisáveis, não scripts avulsos.
- Custo assumido: manter o seed de referência sincronizado com a evolução dos enums dinâmicos — mitigado por teste de integração que valida a presença dos valores obrigatórios após provisionamento.

---

## ADR-0020: Segurança como critério transversal obrigatório

**Status:** Aceita
**Data:** 2026-07-08

### Contexto
Componentes de infraestrutura compartilhada (SDK, SharedKernel) são superfície de ataque de toda a plataforma por definição — uma falha em `Secco.SDK` se propaga a todo produto que o consome. Decisões de design que parecem puramente técnicas (formato de um id, o que logar, o que aceitar de um header) têm consequências de segurança que só aparecem depois, em produção, se não forem avaliadas no momento do design.

### Decisão
Toda análise de design e toda revisão de código na plataforma — feita por mim ou por IA (Claude Code, Claude.ai) — avalia explicitamente os seguintes eixos, quando aplicáveis ao componente em questão, **antes** da implementação:

- **Confiança em input externo:** todo dado vindo de fora do processo (headers HTTP, query strings, payloads, mensagens de fila) é não confiável até validado. Nunca propagar ou persistir sem validação de formato/tamanho.
- **Injeção:** SQL (mesmo com EF Core — atenção a `FromSqlRaw`/SQL cru das procedures da ADR-0017), log forging (CRLF/controle em valores logados), header injection.
- **Vazamento de informação:** o que aparece em mensagens de erro, logs, headers de resposta e stack traces expostos ao cliente. ProblemDetails (ADR-0009) nunca inclui detalhes internos em produção.
- **Multi-tenancy:** todo novo componente de acesso a dado é avaliado quanto a isolamento de tenant (ADR-0005) — a pergunta obrigatória é "este código poderia, por engano, vazar ou aceitar dado de outro tenant?".
- **Autenticação/autorização:** endpoints e chamadas internas novas declaram explicitamente quem pode chamar (ADR-0007); nenhum endpoint "esquece" de proteger por omissão.
- **Negação de serviço:** inputs não confiáveis (headers, batches, listas) têm limite de tamanho/taxa antes de processados ou propagados.
- **Dependências:** pacotes novos adicionados ao `Directory.Packages.props` são avaliados quanto a manutenção ativa e vulnerabilidades conhecidas antes da adoção.

Essa análise é parte do design, não uma revisão posterior: ao propor uma decisão de arquitetura (ex.: formato de um id, política de um header), as opções já vêm acompanhadas do risco de segurança de cada uma — não apenas do trade-off funcional.

### Consequências
- Design de componentes de SDK/SharedKernel passa a incluir explicitamente a pergunta "como isso pode ser abusado?", não só "como isso deve funcionar no caso feliz".
- Checklist de entrega (skill `secco-platform-standards`) ganha item de segurança.
- Custo assumido: análises de design ficam mais longas. Aceito — o custo de uma falha de segurança em componente compartilhado é ordens de magnitude maior.

---

## ADR-0021: Autorização granular — Role + Permission (padrão ASP.NET Core Identity)

**Status:** Aceita
**Data:** 2026-07-09

### Contexto
`role` (ADR-0007) identifica um perfil (`Admin`, `Financeiro`, `Suporte`), mas não expressa ações permitidas com granularidade — a plataforma adota o padrão do `Microsoft.AspNetCore.Identity` (`IdentityRole` + claims de ação): Role é o perfil, Permission é a ação concreta (`invoices:read`, `logs:delete`). Duas estratégias de transporte foram avaliadas: embutir permissões no token (rápido, mas revogação só no expirar do token) ou resolver em runtime a partir do role (revogação imediata, exige consulta/cache). Optou-se pela segunda — a plataforma prioriza revogação imediata de acesso sobre a latência marginal de uma consulta em cache.

### Decisão
- **Token carrega apenas `role`** (ADR-0007) — nunca a lista de permissões.
- **Fonte da verdade do mapeamento Role → Permissions:** `Secco.SecureGate`, por ser o produto de IAM. Mapeamento é **por tenant** (um tenant pode customizar o que um role concede) — coerente com ADR-0005.
- **Formato de permissão:** string `recurso:ação` (`invoices:write`, `logs:delete`) — namespaced por recurso para evitar colisão semântica entre produtos.
- **Resolução:** outras APIs consultam o SecureGate via `Secco.SecureGate.Client` (NSwag, ADR-0006) — nunca `HttpClient` manual.
- **Cache local obrigatório**, chave `(tenant_id, role)`, TTL curto (60–300s, configurável). Motivo: sem cache, toda requisição autorizada dependeria do SecureGate em tempo real, tornando-o gargalo e alvo natural de negação de serviço (ADR-0020).
- **Fail-closed obrigatório:** se o SecureGate estiver indisponível e o cache expirado, a resposta é **negar** o acesso. Autorização nunca falha aberta.
- Exposto via `AddSeccoAuthorization()` no `Secco.SDK`, com policies dinâmicas geradas a partir de constantes de permissão no SharedKernel: `[Authorize(Policy = SeccoPermissions.Invoices.Write)]`.
- Permissões por usuário individual (fora do papel) ficam fora de escopo desta ADR — se necessárias, tratadas como extensão futura, não como caso padrão.

### Consequências
- Revogar acesso de um role é imediato dentro do horizonte do TTL, sem esperar expiração de token.
- SecureGate ganha responsabilidade de servir consultas de permissão em alta frequência (mitigada pelo cache) — reforça a exigência de SLA/HA já registrada na ADR-0007.
- Cache introduz uma janela (o TTL) onde uma permissão revogada ainda pode estar em vigor — aceito conscientemente; TTL deve ser calibrado por sensibilidade da operação (ex.: TTL menor para permissões financeiras).
- Nenhum produto implementa checagem de permissão própria; tudo passa por `AddSeccoAuthorization()`.

---

## ADR-0022: Secco.SecureGate — OpenIddict e identidade como dado de plataforma

**Status:** Aceita
**Data:** 2026-07-13

### Contexto
`Secco.SecureGate` é o único emissor de tokens da plataforma (ADR-0007) e precisa implementar um servidor OIDC completo: fluxos de autorização (authorization code + PKCE, client credentials), emissão e assinatura de tokens, endpoint de descoberta, rotação de chaves via JWKS, revogação e introspecção. Implementar esse protocolo manualmente é exatamente o risco que a ADR-0020 manda evitar — é criptografia aplicada, com margem estreita para erro grave (validação de `nonce`, PKCE, gestão de chaves). Além da biblioteca, é preciso decidir onde vivem usuários e credenciais num mundo database-per-tenant (ADR-0005).

Alternativas avaliadas para a base OIDC:
- **Escrever na mão** sobre ASP.NET Core Identity puro — descartado por ADR-0020 (reimplementar protocolo criptográfico).
- **Duende IdentityServer** (sucessor comercial do IdentityServer4) — licenciamento pago escalando por receita; mesmo padrão de armadilha já registrado para FluentAssertions v8+ (ADR-0012) e MassTransit v9 (ADR-0015).
- **IdentityServer4** — end-of-life, sem patches de segurança desde 2022; descartado por risco ativo.
- **IdP externo hospedado** (Keycloak self-hosted, Auth0/Entra ID como IdP único) — contradiz a ADR-0007 (SecureGate como único emissor); Keycloak introduz stack Java estranha ao ecossistema; SaaS externo cria dependência de vendor incompatível com adotantes self-hosted (mesmo motivo que descartou opções cloud-native na ADR-0015).
- **OpenIddict** — OSS sem tiers pagos (Apache 2.0), manutenção ativa, integração nativa com EF Core e ASP.NET Core Identity (`UserManager`/`RoleManager`), que é o mesmo modelo já adotado pela ADR-0021 para Role + Permission.

### Decisão
- `Secco.SecureGate` usa **OpenIddict** como base do servidor OIDC, integrado a **ASP.NET Core Identity** (`IdentityRole`, `IdentityUserClaim`) — o mesmo modelo da ADR-0021, sem reconciliar dois sistemas de identidade distintos.
- OpenIddict é um **framework, não um produto pronto**: a tela de login, o fluxo de consentimento, o modelo de tenant e as regras de negócio são responsabilidade do SecureGate, construídos sobre ele — a ADR não encerra o trabalho de autenticação, apenas evita reimplementar o núcleo criptográfico do protocolo.
- **Identidade é dado de plataforma, não dado de negócio de tenant**: um banco próprio do SecureGate (`secco_securegate`) contém usuários (com `tenant_id`), roles e permissões **por tenant** (ADR-0021), clients OIDC e o catálogo de tenants (ADR-0005). Nenhum produto acessa esse banco — o consumo é exclusivamente via tokens e `Secco.SecureGate.Client` (ADR-0006).
- **Isolamento de dependência:** OpenIddict só é referenciado pelo `Secco.SecureGate`. As demais APIs seguem validando JWT via `AddSeccoAuthentication()` do SDK (`JwtBearer` padrão, lendo JWKS do SecureGate) — nenhum outro produto depende do OpenIddict, direta ou indiretamente.
- Claims emitidos seguem a convenção curta já fixada na ADR-0007 (`sub`, `role`, `tenant_id`, `scope`).
- **Sequência de entrega**: client credentials + JWKS/discovery primeiro (produtos validam contra Authority real, aposentando a chave HS256 de desenvolvimento); authorization code + PKCE e telas de login entram na sequência, a tempo do AdminPortal (Fase 7), seu primeiro consumidor.

### Consequências
- Evita reimplementar protocolo criptográfico (ADR-0020) e a armadilha de licenciamento comercial já observada em outras dependências; nenhum custo de licença repassado a adotantes.
- SecureGate herda a obrigação de configurar corretamente PKCE, rotação de chaves e expiração de token — responsabilidade real de implementação, não eliminada pela escolha da biblioteca.
- Login resolve o tenant do usuário sem descoberta ambígua (o registro do usuário carrega o tenant).
- O banco do SecureGate vira o dado mais sensível da plataforma: backup, LGPD e HA tratados como plataforma (reforça o SLA superior da ADR-0007).
- Acoplamento a uma biblioteca .NET específica para emissão; migração futura exigiria nova ADR, mas o isolamento (só o SecureGate a referencia) limita o raio de impacto — os demais produtos só falam OIDC/JWT padrão.

---

## ADR-0023: Secco.AdminPortal — Blazor Server como relying party OIDC

**Status:** Aceita
**Data:** 2026-07-14

### Contexto
O `Secco.AdminPortal` (Fase 7) é o console de administração da plataforma e o **primeiro consumidor real do login de usuário** (ADR-0022, Fase 6.5). É também o primeiro produto que **não é uma API de quatro camadas** (ADR-0002): não tem domínio nem banco próprios — orquestra os demais produtos via seus clients (ADR-0006). Duas decisões precisam de registro: a stack de UI e o modelo de autenticação/autorização de um cliente que age em nome de um operador humano sobre múltiplos tenants.

Alternativas de stack avaliadas:
- **Razor Pages / MVC** — server-rendered simples, sem circuito com estado; porém grids/filtros/paginação interativos de um console exigem JavaScript e plumbing manual que o Blazor entrega nativamente.
- **Blazor WebAssembly + BFF** — sensação de SPA, mas exige um Backend-for-Frontend para custodiar tokens (não expô-los no browser, ADR-0020) e um segundo processo — mais superfície para um console interno.
- **SPA externa (React/Angular)** — fora do ecossistema .NET, duplica tooling e afasta o reuso direto dos clients NSwag.
- **Blazor Server** — C# ponta a ponta, reuso direto dos `Secco.*.Client`, tokens custodiados no servidor, interatividade rica sem build JS; custo: circuito SignalR com estado.

### Decisão
- O `Secco.AdminPortal` é uma aplicação **Blazor Server**, um **relying party OIDC** (cliente confidencial) — **não** um resource server. Autentica via **authorization code + PKCE** (ADR-0022) com sessão em **cookie** (`OpenIdConnect` + `Cookie`, `SaveTokens`); **não** usa `AddSeccoAuthentication()` do SDK (validação JWT de resource server não se aplica a um cliente).
- **Não é produto de quatro camadas**: é uma app de apresentação/orquestração sem domínio nem banco próprios. Reusa o cross-cutting não-de-auth do SDK (correlation, resilience, health checks); a auth é a do relying party.
- **Comunicação com os produtos exclusivamente via clients NSwag** (ADR-0006), com um `DelegatingHandler` que anexa o **access token do OPERADOR** às chamadas (**on-behalf-of**): cada ação carrega a identidade e as permissões reais do operador, e cada produto autoriza no seu próprio boundary (ADR-0021). Nada de token de serviço amplo — a auditoria é a pessoa.
- **Operador cross-tenant**: o AdminPortal serve o operador de plataforma, que cria/gerencia tenants, provisiona identidade em qualquer tenant e (Fase 7.3) navega logs de qualquer tenant. Operadores são **usuários com o role `platform-operator`** num **tenant de plataforma bem-conhecido** (seed de referência, Guid fixo). O bootstrap do primeiro operador é processo controlado (seed de DEV cria um; produção provisiona out-of-band).
- **Escalada de privilégio barrada no token (ADR-0020, defesa em profundidade)**: o SecureGate **filtra o scope `securegate:admin` no `/connect/authorize`** — só usuários com o role `platform-operator` o recebem. Login de usuário comum pelo client do AdminPortal **não** produz token administrativo, mesmo que o client tenha o scope permitido. Client credentials (máquinas) seguem gated pelas permissões do client, sem alteração.

### Consequências
- Circuito Blazor Server com estado (SignalR): novo modelo de execução a operar e proteger; escalabilidade limitada por circuito — aceitável para um console interno de baixa escala.
- Tokens do operador custodiados **no servidor** (cookie de sessão com `SaveTokens`), nunca no browser.
- Acoplamento do AdminPortal a `OpenIdConnect`/Blazor Server; por ser produto único e cliente OIDC padrão, o raio de impacto é local.
- **Questão em aberto para a Fase 7.3 (visualização de logs)**: a autorização granular resolve permissões por `(tenant_id, role)` no tenant **alvo** (ADR-0021), mas o role do operador vive no **tenant de plataforma** — um operador cross-tenant não resolve permissões de log no tenant que está inspecionando. A forma de o operador ler logs cross-tenant fica **explicitamente não decidida** aqui e será resolvida na 7.3 (candidatos: uma permissão de plataforma reconhecida pelo produto; um role de operador semeado por tenant; ou um token de serviço com scope de leitura). A gestão de tenants/identidade da 7.1–7.2 não sofre desse problema: é gated por **scope** (`securegate:admin`), não por permissão por tenant.

---

## ADR-0024: Acesso de leitura cross-tenant do operador de plataforma

**Status:** Aceita
**Data:** 2026-07-14

### Contexto
Resolve a **questão em aberto da ADR-0023**. O operador de plataforma (AdminPortal) precisa **ler** dados de qualquer tenant (logs primeiro, Fase 7.3), mas dois mecanismos da plataforma barram isso: (1) a autorização granular resolve permissões por `(tenant_id, role)` no tenant **alvo** (ADR-0021), e o role do operador (`platform-operator`) vive no tenant de plataforma, não no alvo; (2) o token do operador carregaria o claim `tenant_id` da plataforma, que **conflita** com mirar outro tenant via header `X-Tenant-Id` (a regra de precedência da ADR-0005: claim vence, divergência = 400).

### Decisão
- **O token do operador de plataforma NÃO carrega o claim `tenant_id`.** Operador não é dado de um tenant — é uma identidade de plataforma que **escolhe o tenant por requisição** via `X-Tenant-Id`. Isso usa o caminho **"sem claim → header"** que a ADR-0005 **já permite** para identidades sem tenant (o mesmo caminho das máquinas em client credentials) — **sem reformar** a regra de conflito. Usuários comuns seguem com `tenant_id` no token; o isolamento da ADR-0005 permanece intacto: token **com** claim + header divergente continua 400; só tokens **sem** claim (operadores e serviços) usam o header livremente, e esses só são emitidos a identidades privilegiadas assinadas pelo SecureGate.
- **A autorização concede ao papel `platform-operator` um conjunto READ-ONLY fixo** (`log-entries:read`, `log-processes:read`, `api-call-logs:read`) em **qualquer** tenant, via **caso especial na resolução `role → permissions` do SecureGate**: pedida a resolução de `platform-operator`, o SecureGate devolve o read-set independentemente do tenant. A autoridade de IAM decide (ADR-0021); **produtos e SDK ficam inalterados** — o produto só recebe uma lista de permissões e autoriza como sempre, sem saber que é operador.
- **O read-set do operador vive no SecureGate** (política de IAM), referenciando nomes de permissão de produto. É uma **exceção consciente à ADR-0003** (constantes de produto ficam no produto): aqui não é a constante do produto, é a **política de plataforma** sobre o que o operador pode ler — um único lugar, versionado, para evoluir a capacidade.

### Consequências
- O operador lê logs (e futuros dados read-only) de qualquer tenant **on-behalf-of** — a auditoria no produto é a **pessoa** (o `sub` do token), não uma identidade de serviço.
- Capacidade **super-leitor** ampla, mitigada por: ser **somente leitura**; ser gated ao papel `platform-operator` (que o SecureGate só concede a operadores reais, cujo scope admin já é filtrado no login, ADR-0023); e o read-set ser **explícito e num só lugar**.
- **Escrita cross-tenant não é concedida** — o operador inspeciona, não altera dados de tenant alheio. Se algum dia for necessário, exige nova ADR.
- Acoplamento pontual do SecureGate a nomes de permissão de produto no read-set; contido a uma constante de política, evolutível sem tocar produtos.

---

## ADR-0025: Cifragem das connection strings de tenant no banco de plataforma

**Status:** Aceita
**Data:** 2026-07-19

### Contexto
O catálogo de tenants do SecureGate (ADR-0022) custodia a connection string do banco de cada par (tenant, produto) — o dado mais sensível da plataforma. A disciplina write-only (ADR-0020) impede vazamento por API, logs e erros, mas o valor repousa em texto claro na coluna `ds_connection_string`: um backup, dump ou acesso direto ao banco de plataforma expõe as credenciais de banco de **todos os tenants e produtos de uma vez**. Delegar à infraestrutura (TDE, disco cifrado) não é garantia que o produto possa dar: a plataforma é adotável on-prem via NuGet e não controla a infra do adotante — além de o PostgreSQL (segundo provider, ADR-0018) não ter TDE nativo.

### Decisão
Ciframos a connection string **na camada de aplicação do SecureGate**, com **AES-256-GCM** e chave mestra fornecida por configuração:

- **Cifra no write, decifra apenas no caminho de leitura do catálogo** (`catalog:<produto>`). Domínio permanece puro: a cifragem é preocupação de Infrastructure (value converter do EF Core no `SecureGateDbContext` — nenhum caminho do contexto persiste plaintext).
- **Formato versionado e autodescritivo** na coluna: `secco-enc:v1:<base64(nonce ‖ ciphertext ‖ tag)>`. Valor sem o prefixo é tratado como legado em claro: aceito na leitura, **re-cifrado por migração idempotente no startup** (mesma disciplina do seed de referência, ADR-0019). A coluna passa de 2000 para 4000 caracteres (o teto de 2000 segue valendo para o **plaintext**, no domínio).
- **Chave mestra**: `SecureGate:Catalog:EncryptionKey` (32 bytes, base64). Em **Production, ausência de chave é fail-fast** no startup; em Development, uma `DevelopmentEncryptionKey` embutida é permitida — **proibida em Production com fail-fast**, espelhando o padrão da `DevelopmentSigningKey` (ADR-0022).
- **Rotação de chave** suportada pelo versionamento: chaves anteriores ficam em `SecureGate:Catalog:RetiredEncryptionKeys` só para decifrar; o re-encrypt do startup converge tudo para a chave ativa.
- Cifragem de infra (TDE/disco) **continua recomendada como defesa em profundidade** na documentação de deploy — complementa, não substitui.
- Cofre externo de segredos (Key Vault/Vault) fica como **evolução futura** via nova ADR, se algum adotante exigir.

### Consequências
- Backup/dump/acesso SQL ao banco de plataforma deixa de expor credenciais de tenants — o cenário de maior blast radius da plataforma é neutralizado nos dois providers.
- A disponibilidade do catálogo passa a depender da chave: **perda da chave = plataforma sem acesso aos bancos de tenant**. A chave entra no runbook de backup do adotante (documentação de deploy) com o mesmo peso do backup do banco.
- Comprometimento total do host do SecureGate (chave + banco juntos) **não** é coberto — nenhuma cifragem de aplicação cobre; é o cenário do cofre externo futuro.
- Migração transparente: valores legados em claro são aceitos e convergidos no primeiro startup após o upgrade.
- Testes de integração passam a provar que o valor persistido **nunca** é plaintext (asserção direta na coluna).

---

## ADR-0026: Login federado com Microsoft Entra ID por tenant

**Status:** Aceita
**Data:** 2026-07-19

### Contexto
Adotantes corporativos autenticam seus usuários num diretório próprio (Active Directory / Microsoft Entra ID) e esperam usar essas contas — com o MFA e as políticas de acesso condicional já existentes — para entrar nos produtos da plataforma. A ADR-0022 descartou IdP externo **como emissor único**; a ADR-0007 já previa "login federado futuro (Entra ID, OIDC de terceiros)" como compatível com os claims curtos. A questão é federar a **autenticação** sem abrir mão do SecureGate como único emissor, num modelo multi-tenant onde cada tenant é uma empresa com diretório próprio.

Alternativas avaliadas:
- **LDAP contra AD on-premises** — a tela de login validaria usuário/senha por LDAP. Descartado na v1: a senha do diretório do cliente passaria pelo SecureGate (custódia transitória de credencial alheia, ADR-0020), exige conectividade de rede com o DC do cliente e configuração LDAPS correta por tenant. Entraria por nova ADR se um adotante on-prem real exigir.
- **App registration por tenant** (cada tenant registra um app próprio no seu Entra e o SecureGate guarda client id/secret por tenant) — exige esquemas OIDC dinâmicos por tenant (complexidade real no ASP.NET Core) e custódia de segredos de terceiros no banco de plataforma. Descartado na v1; evolução possível por nova ADR.
- **App registration multi-tenant única da plataforma** — um único app (do adotante da plataforma) no endpoint `organizations`; cada empresa cliente consente o app no próprio diretório (admin consent). Um esquema OIDC estático, nenhum segredo de tenant custodiado. **Escolhida.**

### Decisão
- **Federação é só autenticação; o SecureGate permanece o único emissor (ADR-0007/0022 intactas).** O Entra ID prova a identidade do usuário na tela de login; tokens do Entra nunca chegam aos produtos — o fluxo `/connect/authorize` → tokens da plataforma segue idêntico, incluindo o filtro do scope `securegate:admin` (ADR-0023).
- **Opt-in por tenant**: nova entidade `TenantFederation` (`tb_tenant_federations`, 1:1 com tenant) — provedor (`entra-id` na v1), **directory id** (o tenant GUID do Entra da empresa, dado não-secreto) e flag de habilitação. Gestão via `PUT /api/v1/tenants/{id}/federation` idempotente (scope `securegate:admin`), leitura no detalhe do tenant.
- **Uma app registration multi-tenant da plataforma**: configuração `SecureGate:EntraId` (`ClientId`, `ClientSecret`, `Authority` com default `https://login.microsoftonline.com/organizations/v2.0`), um único esquema `OpenIdConnect` estático com `SignInScheme` = cookie externo do Identity. Sem a seção de configuração, o recurso fica desligado (botão não aparece) — mesmo padrão da seção `Secco:SecureGate` em DEV.
- **Usuários pré-provisionados, fail-closed**: o AD **nunca decide quem tem acesso**, só prova identidade. O usuário precisa existir (provisionado por admin, ADR-0022/6.5). Vínculo: primeiro login casa por e-mail **somente se** o `tid` do token do Entra é exatamente o directory id registrado do tenant **do usuário** e a federação está habilitada e o tenant ativo; em seguida persiste o login externo (`tb_user_logins`, provider `EntraId`, chave `{tid}:{oid}`) e os logins seguintes casam pelo `oid` (imutável), não mais pelo e-mail. Qualquer verificação que falhe → recusa com mensagem genérica (não revela existência de conta, ADR-0020).
- **Validação de issuer multi-tenant**: o issuer deve casar o template do Entra com o próprio `tid` do token; o gate real é o pin `tid == directory id` registrado — um diretório qualquer do Entra não autentica usuário de tenant que não o registrou.

### Consequências
- MFA/Conditional Access do cliente valem automaticamente; a plataforma nunca custodia senha do diretório do cliente.
- Login por senha local continua existindo como alternativa (v1); desligar senha para tenant federado é evolução futura.
- O adotante precisa criar **uma** app registration multi-tenant no Entra dele (documentação de deploy); cada empresa cliente faz admin consent no próprio diretório.
- E-mail mutável no diretório do cliente não permite tomada de conta fora do próprio diretório: o casamento por e-mail só ocorre dentro do `tid` registrado do tenant e apenas no primeiro login (depois o vínculo é por `oid`). Dentro do diretório do cliente, quem controla o diretório controla as identidades — fronteira de confiança aceita e documentada.
- Desativar usuário/tenant no SecureGate continua sendo o controle da plataforma — revogação no AD do cliente afeta apenas as próximas autenticações, e vice-versa.
- Dependência `Microsoft.AspNetCore.Authentication.OpenIdConnect` no SecureGate (já usada pelo AdminPortal; nenhum pacote novo no monorepo).
- LDAP/AD on-premises fica explicitamente fora; retorna por nova ADR com adotante real.

---

## ADR-0027: Base compartilhada de testes de integração — Secco.SDK.Testing

**Status:** Aceita
**Data:** 2026-08-29

### Contexto
Quatro projetos — LogStream, SecureGate, NotificationHub e o template — mantêm uma `WebApplicationFactory` de integração praticamente idêntica: container MsSql, migração única guardada por `SemaphoreSlim`, geração de token JWT de teste, chaves de configuração de tenancy e de permissões. As cópias já divergiram: o helper de token existe em dois lugares com formatos diferentes, a constante de assinatura `chave-de-testes-com-32-caracteres!!` aparece literal nas quatro, e o token do template chegou a não carregar `role` — corrigido à mão em 2026-08-29, quando o recurso Sample adotou permissões, o que não remove a causa: continuam sendo cinco cópias a manter em sincronia. O template propaga uma quinta cópia a cada `dotnet new secco-service` — e propaga justamente a defasada, ou seja, a divergência não é estável: cresce. Pela ADR-0013, divergência entre o template e o padrão é bug de prioridade alta.

### Decisão
Um pacote `Secco.SDK.Testing`, com a base `SeccoApiFactory<TProgram>`, passa a ser o único lugar onde essa infraestrutura vive. Complementa a ADR-0012 sem alterá-la: a stack (xUnit + FluentAssertions + NSubstitute + Testcontainers) e os tipos de teste seguem os mesmos — muda apenas **onde a infraestrutura de integração mora**.

- **Publicável desde já** (`MinVerTagPrefix` `sdk-testing/v`), marcado `DevelopmentDependency` para não fluir transitivamente. Motivo: `Secco.Templates` é publicável, então um produto gerado fora do monorepo precisa da base por NuGet — senão o template gera um projeto de teste que não compila.
- **`ConfigureWebHost` é `sealed` na base**; a extensão acontece por hooks (`ConfigureTestConfiguration`, `ConfigureTestServices`, `OnInitializedAsync`). Selar é o que impede o drift de voltar por dentro.
- **`Audience` e `MigrateAsync` são abstratos**: esquecer de definí-los vira erro de compilação, não teste vermelho no CI.
- **Instância isolada por suíte é o padrão** (cada suíte sobe o próprio container; o CI segue hermético). A variável `SECCO_TEST_SQLSERVER` aponta para uma instância externa nas máquinas onde N containers de SQL Server saturam o Docker. `WithReuse(true)` foi descartado: o Ryuk não reapeia containers reusados e o lixo se acumula até limpeza manual.
- **Chave HS256 aleatória por instância** (32 bytes de `RandomNumberGenerator`), nunca constante — ganho central de segurança (ADR-0020). Uma chave de assinatura embutida num pacote publicado é de conhecimento público, e o validador de autenticação valida presença e comprimento, não notoriedade.
- **Nome de database validado por allowlist** antes de entrar em `CREATE`/`DROP DATABASE` (esses comandos não aceitam parametrização), com sufixo aleatório por instância de factory para isolar suítes concorrentes no modo externo.
- **Fora da v1:** fixture de paridade PostgreSQL (duas cópias hoje) — extraída quando doer.

Na mesma entrega, e sob a ADR-0018 (não sob esta), a seleção de provider de banco deixa de ser copiada em quatro `*DatabaseOptions.cs` e vira um seletor por receita no `Secco.SDK.EntityFrameworkCore`: o produto declara o que aplicar (`UseSqlServer`/`UseNpgsql` com o próprio assembly de migrations), o SDK só seleciona — **zero dependência de engine adicionada ao pacote publicado**, preservando a cláusula de extensibilidade da ADR-0018.

### Consequências
- Os dois `JwtTestTokenFactory` estáticos deixam de existir; as chamadas passam a sair da instância da factory.
- O template para de propagar a cópia defasada: passa a referenciar a base, monorepo-first por `ProjectReference` (variante NuGet quando houver adotante externo), no mesmo padrão dos outros dois SDKs.
- Mais um pacote no ciclo de release (ADR-0011) e mais uma superfície pública sujeita a semver (ADR-0009) — mitigado mantendo a lógica de container-vs-instância-externa num tipo `internal`.
- Sem job novo no CI: o filtro `platform` já cobre `src/SDK/**` e `tests/SDK/**` e dispara o `validate-template`.
- Design detalhado, com a tabela de migração consumidor a consumidor, em `docs/superpowers/specs/2026-08-26-secco-sdk-testing-design.md`.

---

## ADR-0028: Provisionamento de banco de tenant — capacidade da plataforma, com automação opt-in

**Status:** Aceita
**Data:** 2026-09-05

### Contexto

A plataforma decidiu database-per-tenant (ADR-0005) e nunca teve como criar esses bancos. O único `CREATE DATABASE` do monorepo estava na infraestrutura de testes; o catálogo do SecureGate cadastra a connection string de um banco que **já existe**, criado por fora. Na prática, os exemplos de adoção usavam `User Id=sa` — e enquanto a connection string de um tenant usa um usuário amplo, o isolamento físico da ADR-0005 é **convenção, não garantia**: um erro de connection string alcança o banco do tenant vizinho, e a aplicação tem no servidor todo o poder daquele usuário.

O primeiro adotante levantou a lacuna ([issue #3](https://github.com/rafsecco/secco-platform/issues/3)) e registrou, em ADR própria, que **não** custodia credencial capaz de criar database "sob nenhuma circunstância". A pergunta que restava era quem preenche a lacuna.

Duas situações reais de adoção pressionam em direções diferentes: o banco do cliente **já existe** e a aplicação precisa apenas de um usuário próprio com o mínimo naquele banco; ou um **tenant novo** entra e alguém precisa criar o banco dele. Tratá-las como uma coisa só força o pior privilégio nas duas.

### Decisão

1. **Provisionamento é capacidade da plataforma e vive no SecureGate**, que já é o registro de (tenant, produto) → banco e já custodia a connection string cifrada (ADR-0025). Nenhum produto consumidor — e nenhum portal — custodia credencial privilegiada de banco.
2. **Dois modos, os mesmos artefatos.** O modo **script** está sempre disponível e é o default: o SecureGate gera o SQL e um DBA aplica. O modo **automático** é **opt-in** por configuração (`SecureGate:Provisioning:Targets:<nome>:AdminConnectionString`); sem a credencial declarada, a automação não existe — fail-closed, nunca degradação silenciosa. O script devolvido é exatamente o que a execução automática aplica: um texto só, para os dois caminhos não divergirem.
3. **O teto do que se concede é `db_owner` no próprio banco do tenant, e nada no servidor.** O usuário criado precisa de DDL porque cada produto roda as próprias migrations; não precisa de mais nada.
4. **Execução síncrona.** Descartado enfileirar num job: a credencial fica no mesmo processo de qualquer forma, então a assincronia moveria *quando* ela é usada, não *onde* mora. O que protege é opt-in + gate `securegate:admin` + conexão isolada existente só nesta operação. Criar database leva segundos, e repetir DDL automaticamente é mais perigoso que útil.
5. **Identificadores por allowlist estrita.** Nome de database e de login não podem ser parametrizados em DDL — entram por concatenação. Formato aceito: `^[a-z][a-z0-9_]{2,62}$`, mais recusa de nomes de sistema, mais delimitação com escape. A validação vem **antes** de qualquer concatenação.
6. **O segredo é gerado no servidor, exibido uma vez, persistido só cifrado.** Senha aleatória criptográfica de alfabeto que não quebra connection string nem literal SQL. Ela aparece apenas dentro do script do modo manual — por necessidade de quem aplica — e nunca é recuperável depois. A connection string resultante vai cifrada para o catálogo e nunca volta em resposta.
7. **Reprovisionar não sobrescreve.** Par (tenant, produto) já cadastrado responde `409`. Rotação de credencial é operação própria e fica fora desta ADR.
8. **O painel de estado dos bancos não usa credencial privilegiada**: sonda cada banco com a conexão de runtime do próprio tenant e responde alcançável/inalcançável com classificação de falha, nunca com a exceção crua.
9. **SQL Server primeiro.** A abstração nasce com dois implementadores previstos (ADR-0018) e um entregue; PostgreSQL vem na rodada seguinte.

### Consequências

- O isolamento entre tenants da ADR-0005 deixa de ser convenção e passa a ser garantia — há teste de integração que conecta com o usuário provisionado de um tenant e prova que o banco do vizinho é inalcançável.
- O adotante cujo DBA não concede `dbcreator` continua atendido: recebe o script correto, com privilégio mínimo por construção, em vez de nada.
- Quem liga a automação passa a ter, no processo do SecureGate, uma credencial capaz de criar databases. É privilégio real e assumido: ele é opt-in, exige `securegate:admin` para ser exercido, e a conexão que o usa existe só durante a operação.
- O modo script grava a connection string no catálogo **antes** de o banco existir. É intencional — a alternativa devolveria ao operador o cadastro manual que a issue quer eliminar — e o painel de estado é quem torna esse intervalo visível.
- `docker-compose.yml` e `.env.example` deixam de ensinar `sa`: cada banco de desenvolvimento passa a ter login próprio com `db_owner` apenas nele, no mesmo modelo que o provisionamento produz.
- Fora desta ADR, registrado: rotação de senha de banco, desprovisionamento, e aplicação de migrations pelo SecureGate (cada produto roda as suas).
- Design detalhado em `docs/superpowers/specs/2026-09-05-provisionamento-banco-tenant-design.md`.
---

## ADR-0029: Canais externos de comunicação corporativa no NotificationHub (Teams, Slack)

**Status:** Aceita
**Data:** 2026-09-05

### Contexto

O `NotificationHub` reconhece um conjunto **fechado** de canais — `email` e `in_app` — e o próprio código explica por quê: diferente de `Source`/`Type`, que são texto livre que o Hub nunca interpreta, um canal mapeia direto para um caminho de código que precisa existir dentro do produto. A consequência é que um adotante não consegue acrescentar canal.

A demanda ([issue #13](https://github.com/rafsecco/secco-platform/issues/13)) é alcançar a ferramenta onde a empresa realmente conversa. Sem isso, os níveis de prioridade do adotante perdem sentido prático: "urgente" e "importante" disparam exatamente os mesmos canais.

**Fatos verificados antes desta decisão**, não presumidos:

- Os Office 365 Connectors do Teams foram **desativados entre 18 e 22 de maio de 2026**. Webhooks de connector existentes deixaram de funcionar. O caminho atual da Microsoft é **Power Automate Workflows**, com o gatilho *When a Teams webhook request is received*, aceitando Adaptive Card ou MessageCard e postando com a identidade do Flow bot.
- Os **incoming webhooks do Slack seguem suportados** e sem aviso de descontinuação na documentação oficial. O Slack recomenda `chat.postMessage` apenas quando é preciso apagar, editar ou rotear dinamicamente entre canais.

Ou seja: as duas ferramentas **não** são equivalentes por ambas aceitarem HTTP. Divergem em mecanismo, formato de payload e ciclo de vida da configuração.

### Decisão

1. **`teams` e `slack` entram como canais nativos**, cada um com provider e tradução próprios. O conjunto de canais **continua fechado**: esta ADR acrescenta dois membros, não abre o conjunto para extensão arbitrária. Canal novo segue exigindo código no produto e ADR.
2. **Não existe canal `webhook` genérico**, e a URL de destino **nunca** vem no payload da notificação. O consumidor pede um canal; quem resolve o destino é o Hub. O contrário transformaria o produto num proxy HTTP dirigido pelo consumidor — SSRF por construção (ADR-0020).
3. **A configuração de destino vive por tenant, no banco do próprio tenant.** O Hub já é database-per-tenant (ADR-0005), então o isolamento vem da estrutura que já existe. Configuração por instalação foi descartada não por conveniência: numa instalação multi-tenant, destino compartilhado significa notificação de um tenant chegando ao canal de outro. Configuração híbrida com fallback foi descartada porque um tenant sem configuração própria passaria a publicar no destino alheio silenciosamente.
4. **Teams via URL de Workflow; Slack via URL de incoming webhook de um Slack app.** Cada provider traduz o modelo interno para o formato da ferramenta — Adaptive Card no Teams, `text`/Block Kit no Slack. O chamador nunca vê essa diferença.
5. **O registro de entrega é a `Notification` generalizada com discriminador de canal**, não uma entidade por canal. O critério é o mesmo que a Fase 8.4 usou para separar `InAppNotification`: ciclo de vida. E-mail, Teams e Slack têm o **mesmo** ciclo — pendente, enviado ou falho, com retry por entrega —, então o mesmo critério que separou o in-app manda juntar estes três.
6. **A entrega externa é assíncrona, com retry por entrega** (ADR-0015 Camada 2), reusando a máquina que o e-mail já usa. In-app segue gravando de imediato. Isto não é padrão novo: é o comportamento que o produto já tem, estendido a mais dois canais.
7. **O segredo de configuração é cifrado em repouso**, no formato versionado `secco-enc:v1:` da ADR-0025. Para isso, o cifrador **sobe do `Secco.SecureGate.Infrastructure` para o SDK**: ele hoje é interno àquele produto, o `secco-intranet` já o reimplementou por conta própria, e o NotificationHub seria a terceira cópia do mesmo formato criptográfico. Três implementações independentes de cifragem divergindo é risco, não conveniência.

### Consequências

- O NotificationHub ganha superfície administrativa própria para configurar canais por tenant (endpoint gated por permissão, ADR-0021) e uma migration nos dois engines.
- **A URL do Workflow do Teams é um segredo operacional com dono.** Um workflow do Power Automate pertence a uma pessoa e fica órfão se ela sair da organização. Isso não é problema de código e não tem solução no Hub: entra como requisito operacional documentado — configurar coproprietário.
- Mensagens no Teams aparecem com a identidade do **Flow bot**; nome e ícone próprios não são suportados por payload de webhook. Quem quiser identidade própria precisa de Bot/Teams App, que é decisão futura com custo desproporcional ao caso atual.
- No Slack, se um dia for preciso **editar, apagar ou rotear dinamicamente** entre canais, o caminho correto passa a ser bot token com `chat.postMessage`. Fica registrado como gatilho, não como dívida.
- A `Notification` deixa de ser "notificação por e-mail" e passa a ser "entrega por canal externo". `Recipient`/`Subject`/`Body` continuam existindo, com significado por canal — no Teams e no Slack, o destino é a configuração do tenant, não um endereço no registro.
- Acrescentar uma terceira ferramenta (Google Chat, por exemplo) repete a forma desta ADR: provider próprio, tradução própria, ADR própria. É deliberado que isso não seja barato — canal barato de adicionar seria canal genérico, que é justamente o que se recusou.
- Design detalhado em `docs/superpowers/specs/2026-09-05-canais-externos-notificationhub-design.md`.
---

## ADR-0030: Soberania da instalação — fronteira entre console de operação e portal do adotante

**Status:** Aceita — parcialmente substituída pela ADR-0051 (papel do AdminPortal e da Intranet como console)
**Data:** 2026-09-12

### Contexto

O `Secco.AdminPortal` nasceu como exercício, para exercitar a fundação (ADR-0023). Quando o primeiro adotante — a Intranet — planejou a própria área administrativa, a pergunta "o que migra do AdminPortal?" travou, e não por questão de UI.

O que travava era **o significado de "administrar todos os tenants"**. Havia três leituras, e cada uma levava a uma arquitetura diferente:

| Leitura | Quem é o operador | O que "todos" significa |
|---|---|---|
| SaaS | a Secco, operando para várias empresas | todas as empresas clientes, numa instalação só |
| Auto-hospedado puro | cada empresa que baixa | nada — cada instalação vê o próprio tenant |
| **Instalação com vários tenants** | **o admin da empresa que instalou** | **os produtos e filiais daquela empresa** |

A preocupação inicial — "duas identidades incompatíveis na mesma aplicação", porque a ADR-0024 decidiu que o token do operador **não** carrega `tenant_id` enquanto um produto tenant-scoped precisa dele — pressupunha a primeira leitura. Sob a terceira, ela encolhe: não são identidades de mundos diferentes, é uma **hierarquia dentro de uma instalação**, o modelo `cluster-admin`/`namespace-admin` do Kubernetes.

### Decisão

- **Cada instalação da plataforma é soberana.** A multi-tenancy existe para que a **empresa que adota** rode vários produtos seus sobre um SecureGate e um LogStream — não para a Secco operar várias empresas. O console não é o portal da Secco: é o portal de quem instalou. Uma empresa pode implantar só o SecureGate, ou só o LogStream, sem tenancy nenhuma.
- **O `Secco.AdminPortal` permanece, com papel redefinido:** deixa de ser "o console de operação da plataforma" e passa a ser o **console mínimo para quem adota a plataforma sem a Intranet**. Quem quer só o SecureGate ainda precisa administrá-lo, e é isso que preserva a adoção independente da ADR-0001.
- **Quem usa a Intranet tem nela o portal completo**, incluindo a área de operação cross-tenant. A duplicação de telas entre os dois é **aceita conscientemente**: é o preço de o SecureGate seguir adotável sozinho.
- **Vocabulário:** `platform-operator` passa a `installation-operator`, e "operador de plataforma" a "operador de instalação". O modelo da ADR-0024 continua servindo inteiro; o nome antigo é que induz ao erro, sugerindo um operador da Secco.
- **Gestão administrativa comum não exige elevação.** Criar e gerir tenant, role e usuário, e provisionar banco (ADR-0028), já são gated por scope `securegate:admin` **sem checar o tenant do chamador** — a superfície de operação cross-tenant que este modelo pede já existe, sem mudança de contrato.
- **Leitura de log cross-tenant exige elevação explícita.** Um usuário de produto tenant-scoped loga com `tenant_id` no token; para ler log de qualquer tenant precisa de um token **sem** esse claim (ADR-0024), e a mesma sessão não carrega os dois. Ao entrar na área de análise, a aplicação pede ao emissor um **segundo token**: sem `tenant_id`, escopo só `logstream`, permissões só de leitura, TTL curto, sem refresh. Esse token nunca é usado nas chamadas do domínio próprio, e é descartado ao sair da área. O formato segue o espírito do [RFC 8693](https://www.rfc-editor.org/rfc/rfc8693) (OAuth Token Exchange) — não é preciso implementá-lo inteiro, mas seguir o padrão evita inventar protocolo.

**Alternativas rejeitadas.** Aposentar o AdminPortal e migrar a operação para a Intranet quebraria a adoção independente da ADR-0001, e "migrar" seria **reescrever**: os dois front-ends não são o mesmo stack (a Intranet é MVC com temas em Razor Class Library, o AdminPortal é Blazor Server). Transformar a operação em CLI também foi descartado: o AdminPortal tem 1222 linhas em 4 páginas, sem domínio nem banco próprios — reescrevê-lo custa mais do que mantê-lo.

### Consequências

- **A ADR-0024 segue válida e intacta.** O token do operador continua sem `tenant_id`, e o read-set cross-tenant continua resolvido como caso especial no SecureGate. Muda o nome do papel, não o mecanismo.
- **A elevação explícita é o que permite um produto tenant-scoped hospedar a área cross-tenant** sem reabrir a ADR-0024. Privilégio passa a ser um **ato**, não um estado — o espírito do `sudo`. Ganhos: o token privilegiado é estreito, curto e separado; a elevação vira evento auditável; e se vazar, o que vaza é leitura de log, não a sessão do usuário.
- **O rename não é find/replace.** O papel é criado pelo seed de referência **por nome**, então trocar a constante criaria um papel novo e deixaria o antigo órfão, com os operadores atribuídos a ele e sem acesso ao console. A convergência renomeia a linha **no lugar**, preservando o `Id` — é o que mantém as atribuições válidas —, no mesmo padrão idempotente que a ADR-0025 usa para converter connection string legada (ADR-0019: o seed de referência roda em todos os ambientes).
- **Busca de log "de todos os tenants de uma vez" é trabalho novo**, não ajuste. Com database-per-tenant (ADR-0005), significa consultar N bancos e unir os resultados na aplicação, com paginação e ordenação sobre a união. É o caso de uso real ("deu erro em algum lugar, não sei onde") e fica no backlog como item próprio.
- **A duplicação de telas entre AdminPortal e Intranet é permanente e bounded** — não é débito a pagar. Quem consolidar as duas quebra a adoção independente.
- O nome e o slug do tenant de plataforma (`Plataforma Secco` / `plataforma`) carregavam a mesma marca enganosa e **foram renomeados junto** (`Instalação` / `instalacao`), pelo mesmo mecanismo de convergência. São display: o tenant é sempre localizado por Guid fixo, e verificou-se que nada no repositório o busca por slug.
- Fica registrado o que **não** decorre desta ADR: o recurso "acessar como" é decisão de segurança independente, permitida **apenas ao admin do tenant** e nunca ao operador de instalação.

---

## ADR-0031: Token exchange como mecanismo de privilégio efêmero

**Status:** Aceita
**Data:** 2026-09-13

### Contexto

A ADR-0030 decidiu que ler log cross-tenant a partir de um produto tenant-scoped exige **elevação explícita**: um segundo token, sem `tenant_id`, escopo só `logstream`, leitura, TTL curto, sem refresh, descartado ao sair da área. Ela definiu a **forma** do token, mas não **quem tem autoridade para obtê-lo** — e é aí que o modelo atual não fecha:

- Um usuário pertence a **exatamente um tenant** (`User.TenantId`, ADR-0022).
- O read-set cross-tenant da ADR-0024 é concedido por **casamento de nome de papel**, sem contexto de usuário, e o que impede abuso é o nome ser **reservado**: `installation-operator` não pode existir em tenant de cliente.

Logo, um usuário da Intranet — que vive num tenant de cliente — **não pode portar o papel que concede leitura cross-tenant**. A autoridade para elevar tem de vir de outro lugar.

O recurso "acessar como" (#6) parece o mesmo problema e não é: é a direção oposta. A elevação **sobe** alcance e atravessa a fronteira de tenant; a representação **desce** privilégio e nunca a atravessa. A #6 barra o operador de instalação justamente porque somar representação a uma identidade sem `tenant_id` abriria caminho para dado de negócio de qualquer tenant. O que as duas compartilham é o **mecanismo**: ambas são privilégio como ato, ambas precisam de um segundo token estreito e curto, e ambas apontam para o [RFC 8693](https://www.rfc-editor.org/rfc/rfc8693).

### Decisão

#### Mecanismo, desenhado uma vez

O **token exchange do RFC 8693** entra no `/connect/token` do SecureGate como `grant_type=urn:ietf:params:oauth:grant-type:token-exchange`, registrado via `AllowCustomFlow` do OpenIddict. Troca de token vive no emissor, e o emissor é único (ADR-0007/0022).

Cada uso do mecanismo é uma **capacidade**, com autoridade própria e allowlist própria. Esta ADR define o mecanismo e a capacidade de **elevação**; a **representação** (#6) fica registrada como capacidade prevista sobre o mesmo grant, com a política em aberto na própria issue.

#### Invariantes de todo token trocado

Valem para qualquer capacidade, presente ou futura, e cada uma corresponde a um teste negativo obrigatório:

1. **Mais estreito, nunca mais largo.** O escopo sai do allowlist da capacidade e **nunca** é herdado do `subject_token`. `securegate:admin` não é emitível por troca, em hipótese nenhuma.
2. **Sem refresh token.**
3. **TTL próprio, com teto.** Configurável para menos, nunca para mais: o teto é constante no código, e a configuração só pode apertá-lo — o mesmo padrão já usado no NotificationHub para tamanho de coluna (issue #23).
4. **Não re-trocável.** Token trocado não serve de `subject_token`. Isso corta a cadeia A→B→C de escalada.
5. **O subject é token de usuário.** Client credentials já emite token tenant-less; máquina não eleva.
6. **Fail-closed com erro genérico.** Sem concessão, concessão expirada, capacidade desconhecida ou subject inválido respondem **igual**, sem revelar se o usuário existe nem por que foi recusado (ADR-0020).
7. **Troca não auditada não acontece.** Ver a política de auditoria abaixo.

#### Capacidade de elevação

**Autoridade — concessão explícita na plataforma.** Uma entidade nova, `ElevationGrant`, no banco de plataforma do SecureGate registra que tal usuário pode elevar. É gerida por quem tem `securegate:admin`, aceita expiração opcional, e **sem linha não há elevação**. A revogação bloqueia a próxima troca imediatamente.

**Token resultante.** `sub` é a pessoa real — a auditoria no produto continua sendo quem agiu, como na ADR-0024. **Sem `tenant_id`**, então o tenant alvo viaja em `X-Tenant-Id` pelo caminho "sem claim → header" que a ADR-0005 já permite, sem reformar a regra de conflito. Escopo só `logstream`. TTL padrão de 15 minutos, teto de 60.

**Papel próprio: `installation-log-reader`.** O token elevado leva esse papel, **carimbado pelo emissor**. Ele não é persistido em `tb_roles`, não é atribuível pela gestão, e seu nome é **reservado** ao lado de `installation-operator` e do legado `platform-operator`. O read-set dele é resolvido no mesmo caso especial da ADR-0024.

Reusar `installation-operator` no token elevado custaria menos e foi **recusado de propósito**: o token afirmaria ser algo que o portador não é, e a trilha não distinguiria um leitor elevado de um operador real — a mesma preocupação de repúdio que a #6 levanta para a representação.

**Read-set menor que o do operador.** O `installation-log-reader` recebe `log-entries:read`, `log-processes:read` e `api-call-logs:read` — **não** recebe `audit-entries:read`, que o operador tem. O caso de uso que motivou a elevação é diagnóstico ("deu erro em algum lugar, não sei onde"), e a trilha de auditoria é dado mais sensível que o diagnóstico. Um usuário de tenant de cliente que eleva não ganha leitura da trilha de auditoria alheia.

**Auditoria — síncrona e fail-closed.** Cada troca grava um `AuditEntry` no LogStream, **no tenant do usuário que elevou**, antes de o token ser emitido: quem, qual capacidade, quando e a partir de qual subject. **Se o registro falhar, a troca é recusada.** Três razões: a issue #2 já fez do LogStream a trilha de auditoria da plataforma, com ingestão síncrona justamente para registro obrigatório não sumir numa fila; uma elevação que não pode ser auditada é exatamente o que a ADR-0020 manda impedir; e registrar no tenant de origem permite aos admins **daquela empresa** verem quem, entre os seus, atravessou a fronteira.

#### Alternativas rejeitadas

- **Conta separada no tenant de plataforma.** Já funciona hoje, sem código — mas quebra a promessa de uma identidade por pessoa que justificou a tenancy (ADR-0030), e a Intranet passaria a exigir segundo login.
- **Permitir `installation-operator` em qualquer tenant.** O mínimo de código, reaproveitando o caso especial existente — mas remove a barreira **estrutural** que protege o read-set (nome impossível em tenant de cliente) e a troca por uma dependência de a gestão nunca conceder o papel por engano. É a opção com risco real de vazamento cross-tenant.
- **Tabela de auditoria local no SecureGate.** Evitaria a dependência nova entre produtos e a indisponibilidade acoplada — mas fragmentaria a trilha que a #2 decidiu ser uma só, e o admin do tenant não veria a elevação no mesmo lugar em que vê o resto.

### Consequências

- **Produtos e SDK ficam inalterados.** O LogStream recebe uma lista de permissões e autoriza como sempre; quem decide que um leitor elevado vê log de qualquer tenant é o IAM, no mesmo caso especial da ADR-0024.
- **Dependência nova: o SecureGate passa a chamar o LogStream** para gravar a auditoria da troca, via client credentials. Até aqui o SecureGate **não dependia de produto nenhum** — LogStream e AdminPortal dependem dele, e ele de ninguém —, então deixa de ser a folha do grafo de dependências. Não há deadlock — são requisições distintas —, mas passa a haver ordem de subida a respeitar.
- **LogStream indisponível implica elevação indisponível.** Aceito: elevar é ato raro e deliberado, falhar fechado é o lado seguro, e login e uso normal **não** são afetados.
- **Revogação tem janela de até um TTL**: a concessão revogada impede a próxima troca na hora, mas o token já emitido vale até expirar. O teto de 60 minutos limita essa janela — o mesmo compromisso que a ADR-0021 aceita para a revogação de permissão.
- **Passam a existir três nomes de papel reservados**, e nenhum pode ser criado pela gestão em tenant algum.
- **A bateria de testes negativos é parte da decisão, não da implementação.** A capacidade só pode ser considerada entregue com testes provando que: usuário sem concessão não troca; concessão expirada ou revogada não troca; token trocado não é re-trocável; token de client credentials não serve de subject; o token emitido não carrega `securegate:admin` nem escopo além de `logstream`; nenhum refresh token é emitido; o TTL não passa do teto mesmo com configuração maior; o leitor elevado **não** lê `audit-entries`; falha de auditoria recusa a troca; e as respostas de recusa são indistinguíveis entre si.
- **A #6 herda o mecanismo e não herda a política.** Quando a representação for decidida, ela ganha autoridade própria (admin do tenant), `sub` do alvo com claim `act`, e a restrição intra-tenant — e continua respondendo às próprias perguntas em aberto, a começar pela existência de hierarquia no modelo.


### Emenda de 2026-09-13 — de onde a auditoria da troca escreve

A decisão de auditoria acima tem uma lacuna, encontrada ao fixar o contrato de implementação e **antes de qualquer código**: gravar `AuditEntry` no tenant de quem elevou exige que o SecureGate **escreva em qualquer tenant**, e isso a ADR-0024 proíbe textualmente — *"escrita cross-tenant não é concedida; se algum dia for necessário, exige nova ADR"*. Permissão de escrita de máquina hoje só existe por papel dentro de um tenant específico. O texto original desta ADR fica intacto acima; o que segue o completa.

**Decisão.** Uma identidade de serviço do SecureGate recebe **exatamente uma** permissão cross-tenant de escrita — `audit-entries:write` — e nada mais:

- É um client OIDC reservado, com grant client credentials, escopo `logstream` e o papel **`installation-auditor`**, também reservado.
- O caso especial fica no mesmo lugar do read-set da ADR-0024: resolvido `installation-auditor`, o SecureGate devolve `audit-entries:write` em qualquer tenant. Produtos e SDK seguem inalterados.
- Esta emenda é a "nova ADR" que a ADR-0024 exige para escrita cross-tenant, com escopo **mínimo e nomeado**: uma permissão, uma identidade, um propósito. Não abre precedente para escrita cross-tenant em geral — qualquer outra continua exigindo ADR própria.

**Por que isso é seguro, e não só conveniente.** O SecureGate já é a raiz de confiança da plataforma: quem o compromete emite token para qualquer usuário, de qualquer tenant, com qualquer papel. Dar a ele escrita de auditoria **não amplia** o que um atacante no SecureGate consegue fazer. A mesma permissão numa identidade de produto seria outra conversa.

**Três salvaguardas, cada uma com teste:**

1. **O papel nunca chega a um client pela gestão.** Hoje isso é estrutural: os papéis de client OIDC (`ds_roles`) só são escritos por seeders, e não existe endpoint para geri-los. Se um dia existir, os nomes reservados têm de ser recusados ali também — do contrário, quem cria um client com `installation-auditor` ganha escrita de auditoria em todos os tenants.
2. **O segredo nunca vem do seed de referência.** O seed de referência roda em todos os ambientes (ADR-0019), e segredo semeado seria segredo conhecido em produção. A credencial vem de configuração.
3. **Sem a credencial configurada, não há elevação.** Sem identidade de auditoria não há como auditar, e pela invariante 7 troca não auditada não acontece. A capacidade é, portanto, **opt-in por configuração e fail-closed** — o mesmo padrão da automação de provisionamento (ADR-0028) e da federação (ADR-0026), cuja seção ausente desliga o recurso.

**O que muda nas consequências.** Os nomes de papel reservados passam de três para **quatro**: `installation-operator`, o legado `platform-operator`, `installation-log-reader` e `installation-auditor`. E à bateria de testes negativos somam-se: o `installation-auditor` recebe **apenas** `audit-entries:write`, sem nenhuma leitura; nenhum outro papel ganha escrita cross-tenant; e, sem a credencial de auditoria configurada, toda troca é recusada.

**Correção factual, também de 2026-09-13.** A decisão acima diz que o grant é registrado "via `AllowCustomFlow` do OpenIddict". Está errado. Eu confirmei que `AllowCustomFlow` existe no OpenIddict 7.5.0, mas não procurei suporte nativo — e ele existe: `AllowTokenExchangeFlow()`, com o handler `ValidateSubjectToken`, que valida o token de entrada e entrega o principal já validado em `SubjectTokenPrincipal`. O grant usa o suporte **nativo**. A consequência é de segurança, não de estilo: a validação criptográfica do `subject_token` — a parte mais fácil de errar num handler escrito à mão — passa a ser do OpenIddict.

Isso **não** dispensa verificação. A documentação desses handlers é `<inheritdoc/>` e não diz o que exatamente é checado, então a bateria de testes negativos passa a provar também o que o nativo recusa: `subject_token` forjado, expirado, de outro emissor, malformado, e refresh token apresentado no lugar de access token. O que o nativo não cobrir vira checagem explícita. As invariantes de política — escopo por allowlist, sem refresh, TTL com teto, não re-trocável, subject de usuário, auditoria — continuam sendo desta ADR, e não do OpenIddict.

---

## ADR-0032: Versão de sessão e revogação efetiva

**Status:** Aceita
**Data:** 2026-09-17

### Contexto

A plataforma emite dois tipos de credencial de usuário (ADR-0022): um **refresh token** deslizante (14 dias, renovado a cada uso) e um **access token JWT** que os produtos validam **localmente**, pela assinatura, sem consultar o SecureGate (ADR-0007). O access token dura 60 minutos por padrão.

Até 2026-09-16 a plataforma aprendeu a **cortar a renovação**: bloqueio, desativação, tenant inativo e perda do perfil de operador passaram a ser rechecados a cada renovação. Mas "cortar a renovação" não encerra uma sessão comprometida, por quatro motivos verificados no código:

1. **O access token já emitido segue válido** até expirar. Quem roubou um token segue acessando os produtos por até 60 minutos depois de a conta ser desativada ou de a senha ser trocada — e nenhum produto tem como saber.
2. **Nada revoga os tokens do OpenIddict.** Um refresh token roubado continua trocável até a próxima checagem de estado; e as mudanças que não alteram o *estado* da conta — trocar a senha, trocar o e-mail, suspeitar de comprometimento — não fazem a renovação falhar.
3. **O cookie de login do SecureGate não revalida nada.** O `/connect/authorize` aceita o cookie (válido por 1 hora) e só verifica se o usuário existe: o SecureGate usa `AddIdentityCore` com cookie configurado à mão, sem o `SecurityStampValidator` que o `AddIdentity` liga. Quem tem o cookie obtém **tokens novos** sem digitar senha, mesmo depois de qualquer revogação.

4. **A sessão local das aplicações clientes não é token.** A Intranet usa o id_token só para criar o próprio cookie e não guarda token nenhum; o AdminPortal mantém um cookie de sessão próprio. Revogar tokens na plataforma não encerra essas sessões — o usuário desativado segue dentro da aplicação até o cookie dela expirar.

A gestão de credenciais que vem a seguir (convite, redefinição e troca de senha, troca de e-mail) só é segura se "encerrar as sessões" significar encerrá-las de fato — em todos os produtos, em segundos, inclusive para um access token já emitido.

Alternativas avaliadas:

- **Introspecção a cada requisição** (o produto pergunta ao SecureGate se o token ainda vale). Correta e imediata, mas faz do SecureGate dependência síncrona de toda requisição de todo produto — gargalo e alvo natural de negação de serviço, exatamente o que a ADR-0021 recusou para as permissões ao exigir cache.
- **Lista negra de tokens revogados** distribuída aos produtos. Cresce com cada revogação, precisa de propagação e expurgo, e revoga token a token quando o que se quer é revogar a pessoa.
- **Só encurtar o access token.** Nenhuma mudança nos produtos, mas a janela continua sendo o TTL inteiro — e encurtar demais multiplica renovações.
- **Versão de sessão por usuário, verificada com cache.** O token carrega a versão da sessão em que foi emitido; o produto compara com a versão atual, obtida do SecureGate com o mesmo desenho de cache e fail-closed da ADR-0021. **Escolhida, combinada com access token curto** como segunda barreira.

### Decisão

**Versão de sessão**

- Todo token **de usuário** — access token e id_token de login e renovação, e o token de elevação da ADR-0031 — carrega a claim curta **`sver`**: um resumo (hash truncado) do `SecurityStamp` do ASP.NET Identity. Nunca o stamp cru: ele participa da geração dos tokens de recuperação de senha do Identity.
- Token de máquina (client credentials) **não** carrega `sver`: não é sessão de pessoa. Revogar um client é rotacionar o secret dele.
- O `SecurityStamp` já existe no Identity; nenhuma coluna nova.

**Verificação nos produtos**

- O SecureGate expõe `GET /api/v1/authorization/users/{sub}/session-version` (scope `authorization:read`, o mesmo da resolução de permissões): devolve a versão atual, ou **revogado** se a conta está desativada, bloqueada ou pertence a tenant inativo.
- `AddSeccoAuthentication()` (SDK) confere, depois de validar a assinatura, todo token que tenha `sver`: versão divergente ou revogada → **401**.
- Cache por `sub` com TTL curto e configurável (`Secco:Authentication:SessionVersionCacheTtlSeconds`, padrão 60s). **Fail-closed**: SecureGate indisponível com cache vencido → 401.
- Token **sem** `sver` passa: são os emitidos antes desta decisão, e a ausência não é forjável porque o token é assinado.
- O resolvedor remoto é registrado pela mesma chamada que os produtos já fazem para permissões (`AddSecureGatePermissionResolver()`). Um produto com o SecureGate configurado não fica sem verificação por esquecer uma linha. Sem SecureGate configurado (DEV standalone), a verificação fica desligada, como o resolvedor de permissões por configuração.

**Sessão local das aplicações clientes**

- O SDK oferece a validação de sessão para aplicações que autenticam por cookie a partir do login OIDC (a Intranet, o AdminPortal): a cada requisição HTTP, a `sver` guardada no cookie (vinda do id_token) é conferida pelo **mesmo resolvedor com cache**; divergente ou revogada → o cookie é rejeitado e a pessoa volta ao login.
- Ligar a validação sem resolvedor registrado **derruba o startup** — nunca fica silenciosamente desligada.
- Em Blazor Server a conferência acontece nas requisições HTTP (carga e reconexão do circuito), não dentro de um circuito já aberto; ali quem corta é a recusa das chamadas às APIs e da renovação.

**Revogação**

- Operação única no SecureGate, nesta ordem: (1) **trocar o `SecurityStamp`**; (2) **revogar autorizações e tokens** do usuário no OpenIddict (`RevokeBySubjectAsync` nos dois gerenciadores). O stamp vem primeiro: se o segundo passo falhar, a sessão já está cortada nos produtos, e a falha sobe como erro — falhar a meio caminho deixa a conta mais fechada, nunca mais aberta.
- O `/connect/authorize` passa a validar o **stamp do cookie contra o banco** e o **estado da conta** (a mesma guarda da renovação). Divergência apaga o cookie e exige novo login.
- Gatilhos: "encerrar sessões" pelo admin (`POST /api/v1/tenants/{tenantId}/users/{userId}/sessions/revoke`), desativação de usuário, remoção efetiva de perfil e — nas entregas seguintes — troca de e-mail e todo evento de senha. Tenant desativado **não** revoga usuário a usuário: o endpoint de versão responde revogado para todos os usuários dele.

**Access token curto**

- O padrão de `SecureGate:Tokens:AccessTokenLifetimeMinutes` passa de 60 para **5**, configurável.
- O AdminPortal deixa de guardar o access token no cookie: os tokens vão para um armazenamento no servidor, na chave de uma sessão aleatória, e o `OperatorTokenProvider` renova perto do vencimento — uma renovação por vez por sessão, porque o refresh token é rotativo.

### Consequências

- Revogar passa a valer em **todos os produtos em até um TTL de cache** (60s por padrão), inclusive contra access token já emitido e contra cookie de login roubado. O TTL de 5 minutos é a segunda barreira, para produto que ainda não atualizou o SDK.
- O SecureGate ganha mais uma consulta de alta frequência (uma por usuário ativo por TTL) — mitigada pelo cache, e mesma exigência de disponibilidade já registrada nas ADRs 0007 e 0021. **SecureGate fora do ar derruba o acesso de usuários** assim que o cache vence; é a postura fail-closed escolhida.
- **Todo produto precisa atualizar `Secco.SDK.AspNetCore` e `Secco.SecureGate.Client`** para ganhar a verificação; o `secco-intranet` inclusive, que além disso liga a validação de sessão do cookie — sem ela, a revogação não alcança quem já está logado na Intranet.
- **Cliente que não renova token perde acesso a cada 5 minutos.** Mudança de comportamento registrada no CHANGELOG, com a configuração para voltar a 60.
- O AdminPortal ganha armazenamento de sessão no servidor: reiniciá-lo encerra as sessões dos operadores, e **mais de uma instância exige cache distribuído** — registrado, fora desta decisão.
- Mudança de papel, de permissão ou de dado cadastral que **não** passe pela operação de revogação continua valendo na próxima renovação ou no TTL de permissões da ADR-0021, como hoje.
- Complementa a ADR-0007 (nova claim curta) e a ADR-0021 (mesmo desenho de cache fail-closed, agora por usuário); não substitui nenhuma.

---

## ADR-0033: Capacidade de e-mail da plataforma e ciclo de credencial do usuário

**Status:** Aceita
**Data:** 2026-09-17

### Contexto

Desde a ADR-0022 o usuário é **provisionado por admin**, e a senha inicial é digitada por quem cria a conta (`POST /api/v1/tenants/{id}/users`, campo `password`). Três consequências disso nunca foram resolvidas:

1. **O admin conhece a senha da pessoa.** Não há um só momento em que a credencial pertença apenas ao dono, e a senha trafega pela tela de administração, pelo histórico do formulário e, quase sempre, por uma mensagem de WhatsApp ou e-mail escrita à mão.
2. **Não existe recuperação de conta.** Sem "esqueci minha senha", perder a senha significa pedir a um admin que a redefina — de novo conhecendo a nova.
3. **Hoje o usuário não troca a própria senha nem consegue pedir a troca.** Não existe tela para nenhum dos dois.

A ADR-0032 deu à plataforma a revogação efetiva de sessões; ela é pré-requisito do que falta aqui: todo evento de senha precisa encerrar as sessões abertas, ou trocar a senha não expulsa quem entrou com a antiga.

Resolver isso exige que o SecureGate **envie e-mail** — capacidade que ele não tem. O `Secco.NotificationHub` já envia, com `IEmailSender` e dois adaptadores (SMTP/MailKit e SendGrid) dentro da própria Infrastructure.

Alternativas avaliadas para o envio:

- **SecureGate chama o NotificationHub.** Reusa o que existe, mas torna o NotificationHub **obrigatório** para quem só quer identidade — contra a promessa de produtos adotáveis de forma independente (issue #4/ADR-0030) — e, pior, o **link de redefinição ficaria gravado** na tabela de notificações do tenant, legível por quem tem `notifications:read`. Um link de recuperação em repouso, visível a terceiros, anula a recuperação.
- **Duplicar os adaptadores no SecureGate.** Zero acoplamento e zero refactor, ao custo de duas cópias que divergem em silêncio na primeira correção.
- **Extrair para um pacote fino do SDK.** Mesmo caminho já percorrido pelo `SeccoClientCredentialsHandler`, promovido a `Secco.SDK.ClientCredentials` quando surgiu o segundo consumidor. **Escolhida.**

Alternativas avaliadas para os links de convite e redefinição:

- **Tabela própria de tokens** (hash, validade, marca de uso): explícita e auditável, ao custo de migration nos dois engines, código de expurgo — e sem resolver as chaves de Data Protection, das quais os **cookies** já dependem.
- **Tokens do ASP.NET Identity com as chaves de Data Protection em arquivo:** sem pacote nem tabela, mas transfere para o deploy a obrigação de um volume persistente e compartilhado; sem ele, todo link morre a cada reinício e nada avisa.
- **Tokens do Identity com as chaves no banco do SecureGate.** O token já embute o `SecurityStamp` — definir ou trocar a senha invalida os links pendentes sem estado adicional — e a persistência corrige de passagem os cookies. **Escolhida.**

### Decisão

**Capacidade de e-mail**

- Nasce **`Secco.SDK.Email`**: porta `ISeccoEmailSender` e adaptadores **SMTP (MailKit)** e **SendGrid**, com options de provider selecionável. O `Secco.NotificationHub` adota o pacote e apaga suas cópias; o SecureGate passa a usá-lo.
- Cada produto mantém **sua própria seção de configuração** (`NotificationHub:Email`, `SecureGate:Email`): o pacote entrega tipos e adaptadores, não a chave.
- No SecureGate a seção é **obrigatória fora de Development** (fail-fast no startup): sem e-mail não há convite nem recuperação, e o admin não pode definir senha por decisão desta ADR.
- O SecureGate **não** envia pelo NotificationHub, e **não** passa a depender dele.

**Ciclo de credencial**

- **O admin nunca define nem vê senha.** O campo `password` sai do `CreateUser` (quebra de contrato assumida, pacotes em 0.x). A conta nasce sem senha e a pessoa a define pelo **convite** (link de 72 h).
- **Recuperação:** página pública "esqueci minha senha", com link de **30 minutos**, resposta **sempre idêntica** (exista a conta ou não, dentro ou fora do limite) e **limite por conta e por IP**.
- **O usuário troca a própria senha**, numa página do SecureGate, informando a senha atual — sem ela, uma sessão
  sequestrada trocaria a senha e expulsaria o dono. E **qualquer pessoa pode pedir a troca** pelo "esqueci minha
  senha", sem depender de admin.
- **Redefinição pelo admin** dispara o link para a pessoa e **revoga as sessões na hora**, porque o pedido costuma nascer de suspeita de comprometimento.
- **Todo evento de senha revoga sessões** pela operação única da ADR-0032, registra na trilha e avisa o dono por e-mail. Na troca feita pelo próprio usuário, o cookie dele é renovado em seguida: as outras sessões caem, a dele continua.
- **Uso único** sem tabela: o token embute o `SecurityStamp`, e definir ou trocar a senha muda o stamp — todos os links pendentes daquela conta morrem juntos.
- As páginas (`/conta/esqueci`, `/conta/definir-senha`, `/conta/redefinir-senha`, `/conta/trocar-senha`) vivem **no SecureGate**, com antiforgery. Não há endpoint de API equivalente: um endpoint público que aceita token e senha é convite a automação.

**Conta sem senha local**

- `tb_users` ganha `fl_local_login_enabled` (padrão ligado). Desligado significa **"entra só pelo diretório"** (ADR-0026): sem senha, sem convite, sem recuperação — e a mesma resposta genérica em todos esses caminhos.
- Desligar em conta que já tinha senha **apaga a senha e revoga as sessões**; senão a credencial antiga continuaria valendo numa conta que a empresa decidiu ser só corporativa.
- O tenant federado pode ter os dois modos ao mesmo tempo: o funcionário entra pelo Entra, o terceirizado sem conta no diretório recebe convite.

**Infraestrutura dos links**

- Tokens do Identity em dois provedores com validades próprias (convite 72 h, redefinição 30 min).
- **Chaves de Data Protection persistidas no banco do SecureGate.**
- A URL do link vem de **`SecureGate:PublicBaseUrl`**, nunca do header `Host` — um `Host` forjado faria a plataforma enviar à vítima um link que aponta para o servidor do atacante.

**Auditoria**

- Quatro eventos vão para a trilha do LogStream pela identidade de auditoria que a ADR-0031 já criou: senha definida/trocada/redefinida, convite enviado ou reenviado, pedido de recuperação e falha de validação de link.
- **Best-effort:** falha de auditoria não impede a operação — recuperar conta e revogar sessão não podem ficar reféns do LogStream. Só a elevação (ADR-0031) segue fail-closed, por ser privilégio entre tenants.
- A configuração passa a ser lida de **`SecureGate:Audit`**, aceitando o nome antigo `SecureGate:ElevationAudit` com aviso no startup.

### Consequências

- **Quebra de contrato:** `CreateUser` sem `password`; `Secco.SecureGate.Client` vai a 0.9.0. No monorepo só o AdminPortal chama; o `secco-intranet` não usa.
- **O SecureGate passa a exigir configuração de e-mail** fora de Development. Instalação sem SMTP nem SendGrid não sobe — consequência direta de o admin não poder mais definir senha.
- **Duas dependências novas** no SecureGate (MailKit e SendGrid, via `Secco.SDK.Email`) e uma no pacote (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`). MailKit e SendGrid já eram usadas pelo NotificationHub; nenhuma é nova no monorepo.
- **Uma coluna nova** (`fl_local_login_enabled`) e a **tabela de chaves** do Data Protection, com migrations nos dois engines.
- Os **cookies de login param de quebrar** a cada reinício ou instância nova — efeito colateral desejado da persistência das chaves.
- **As chaves ficam em claro no banco**, ao lado do hash das senhas que protegem o mesmo domínio. Cifrá-las em repouso exige ADR própria e fica registrado como fora de escopo.
- O e-mail vira **dependência de disponibilidade da recuperação de conta**: provedor fora do ar significa ninguém recuperando senha naquele intervalo. O caminho de contorno continua sendo o admin, que dispara novo link — nunca uma senha.

---

## ADR-0034: Idempotência em endpoints de escrita

**Status:** Aceita
**Data:** 2026-09-19

### Contexto

A plataforma nunca escreveu o que uma escrita repetida pode ou não causar, mas já depende disso em dois pontos:

1. **O SDK repete requisições sozinho.** `AddSeccoResilience()` aplica retry automático a `GET`, `HEAD`, `OPTIONS`, `PUT` e `DELETE` — os métodos idempotentes da RFC 9110 — e nunca a `POST`. Um `PUT` ou `DELETE` que não seja idempotente de fato já é um defeito latente: basta um timeout para ele rodar duas vezes.
2. **A entrega da ADR-0033 cria `POST`s cujo efeito sai da plataforma** — convite, redefinição de senha, "esqueci minha senha". Duplo clique ou reenvio do formulário vira e-mail duplicado, e e-mail não se desfaz.

Na prática o código já segue um padrão implícito: criação com chave natural única responde `409` (e-mail do usuário, slug do tenant, nome do perfil), atribuições e desativações repetidas não mudam nada, e a remoção de perfil só revoga sessões quando de fato removeu (ADR-0032). Falta virar regra verificável.

Alternativas avaliadas:

- **Header `Idempotency-Key` em toda escrita**, com a resposta gravada por chave (padrão de gateway de pagamento). Repetição devolve a mesma resposta, inclusive depois de falha de rede. Exige armazenamento por chave — tabela ou cache distribuído (ADR-0035) —, middleware no SDK, limites de tamanho e expurgo, e disciplina de todo chamador; e protege um cenário que a plataforma ainda não tem: `POST` sem chave natural repetido automaticamente.
- **`Idempotency-Key` só onde há efeito externo.** Menos superfície, mesmo armazenamento e mesmo middleware.
- **Idempotência natural como regra escrita**, sem infraestrutura nova. **Escolhida.**

### Decisão

A idempotência é medida pelo **efeito**, não pela resposta: repetir pode devolver outro status (um `DELETE` repetido pode responder `404`), mas nunca pode deixar outro estado nem disparar de novo um efeito colateral.

- **`PUT` e `DELETE` são idempotentes de fato.** O SDK os repete automaticamente; qualquer um que não seja é defeito, não escolha de design.
- **`POST` de criação tem chave natural única** e responde `409` à duplicata — nunca cria um segundo recurso. Recurso sem chave natural candidata é sinal para repensar o endpoint (virar `PUT` na chave do chamador) antes de aceitar a duplicação.
- **`POST` de comando sobre estado** (ativar, desativar, atribuir, remover, ligar e desligar login local) leva ao mesmo estado final quando repetido, e **os efeitos colaterais — revogar sessão, enviar e-mail, registrar auditoria — só disparam na transição real**. Repetir uma remoção já feita não revoga de novo.
- **`POST` cujo propósito é o efeito externo** (reenviar convite, redefinir senha, "esqueci minha senha") não é idempotente por natureza e não finge ser. A proteção é outra: **limite de taxa** obrigatório em todo endpoint desse tipo que aceite chamada anônima, **nenhum estado divergente** entre uma chamada e duas (os links convivem e morrem juntos no uso, pelo `SecurityStamp`), e a tela **desabilita o envio enquanto a requisição está em curso**.
- **Retry automático de `POST` segue proibido** no SDK. Um produto que precise dele sobrescreve o predicado conscientemente, e só para `POST` que cumpra a regra de comando ou de criação acima.
- Todo `PUT`, `DELETE` e `POST` de comando novo acompanha **teste de repetição**: chamar duas vezes, conferir o estado e a ausência do segundo efeito colateral (ADR-0012).

### Consequências

- Nenhuma dependência, tabela ou middleware novo; a regra entra no checklist da skill `secco-platform-standards`.
- **Conhecido e aceito:** o `POST /api/v1/notifications` do `Secco.NotificationHub` cria uma notificação por chamada, sem chave natural — reenvio do chamador vira e-mail duplicado. O produto ainda não tem consumidor real; quando tiver, a decisão entre chave fornecida pelo chamador e `Idempotency-Key` vira ADR própria.
- **Falha de rede no meio de um `POST`** deixa o chamador sem saber se o efeito aconteceu. Para criação, reenviar é seguro (`409` indica que já existe); para efeito externo, o pior caso é um e-mail a mais, limitado pela taxa.
- **Segurança (ADR-0020):** endpoints de efeito externo são vetor de spam e de negação de serviço contra a caixa de terceiros — por isso o limite de taxa é obrigatório, não opcional. Não guardar chaves de idempotência também elimina uma superfície: input do chamador persistido, com tamanho e expurgo a controlar.

---

## ADR-0035: Cache e estado compartilhado entre instâncias

**Status:** Aceita
**Data:** 2026-09-19

### Contexto

A plataforma nunca rodou com mais de uma instância de um mesmo produto, e nada impede que um adotante rode. Hoje convivem em memória de processo coisas de natureza muito diferente:

- **Cache derivado com TTL:** permissões por `(tenant_id, role)` (ADR-0021), versão de sessão (ADR-0032), catálogo de tenants no `Secco.SecureGate.Client`. Perder ou divergir entre instâncias só custa uma consulta a mais, e o TTL curto limita a divergência.
- **Estado que precisa ser visto por todas as instâncias:** o cofre de sessões do operador no AdminPortal (ADR-0032), já escrito sobre `IDistributedCache` com provider em memória. Com duas instâncias, o operador cai toda vez que o balanceador troca de instância — em silêncio.
- **Estado durável:** tokens do OpenIddict, filas do Hangfire e, com a ADR-0033, as chaves de Data Protection do SecureGate — todos no banco do produto.
- **Contadores de limite de taxa** do limitador nativo do ASP.NET, que vivem só no processo.

Redis estava no backlog como "estratégia de cache distribuído", mas nenhum cenário atual exige cache distribuído: o problema real é **estado** mal classificado, não cache lento.

Alternativas avaliadas:

- **Adotar Redis agora** como dependência de todos os produtos. Resolve a multi-instância de uma vez, ao custo de uma dependência de infraestrutura para quem roda uma instância só — e, se a autorização passar a depender dele, de mais um ponto de falha no caminho de toda requisição.
- **Invalidação ativa** (pub/sub avisando as instâncias quando permissão ou sessão muda). Encurta a janela de divergência abaixo do TTL, mas torna o Redis dependência de disponibilidade da autorização, e a ADR-0021/0032 já limitam a janela com TTL curto e fail-closed.
- **Classificar o que existe e fixar onde cada classe mora**, com Redis como provider escolhido para quando a multi-instância for real. **Escolhida.**

### Decisão

Tudo que um produto guarda fora da requisição cai em uma de três classes:

1. **Cache derivado** — reconstruível a partir da fonte, com TTL. Fica **em memória de processo**, por instância; o **TTL é o contrato de invalidação** e não há invalidação ativa. Cache que decide acesso segue as regras da ADR-0021/0032: TTL curto e fail-closed.
2. **Estado efêmero compartilhado** — perde-se sem dano ao reiniciar, mas precisa ser o mesmo em todas as instâncias (cofre de sessões). Sempre atrás de **`IDistributedCache`**, com provider em memória por padrão. **Mais de uma instância exige provider distribuído**, e o README do produto diz isso explicitamente.
3. **Estado durável** — precisa sobreviver a reinício (chaves de Data Protection, tokens, filas). Vai para **o banco do produto**, nunca para cache.

Regras adicionais:

- **Redis é o provider distribuído da plataforma**, via `Microsoft.Extensions.Caching.StackExchangeRedis`, e entra **opt-in por produto** quando houver multi-instância real — não nesta ADR.
- Ao adotar Redis: conexão com **TLS e autenticação**, **prefixo de chave por produto**, e todo valor da classe 2 que contenha token ou segredo **cifrado com Data Protection antes de ir ao cache** — o cofre do AdminPortal guarda refresh tokens, e um dump do Redis não pode entregá-los.
- **Limite de taxa é por instância**, de propósito: com N instâncias o limite efetivo vira N vezes o configurado. É mitigação, não garantia — a proteção contra enumeração do "esqueci minha senha" é a resposta idêntica (ADR-0033), não o limite.

### Consequências

- Nenhuma dependência nova agora; os caches de classe 1 do SDK e do `Secco.SecureGate.Client` ficam como estão.
- **O AdminPortal é o único produto que não escala horizontalmente sem Redis**: além do cofre (classe 2), ele não tem banco para as chaves de Data Protection dos cookies (ADR-0023), então as duas coisas vão para o provider distribuído no mesmo movimento. Até lá, uma instância — e reiniciar derruba as sessões dos operadores, como já registrado na ADR-0032.
- Uma instância nova de qualquer produto passa pelas mesmas perguntas: o que guardo, em que classe cai, onde mora. Estado sem classe é defeito de design.
- **Segurança (ADR-0020):** não haver invalidação ativa mantém a autorização sem dependência de Redis — a queda dele nunca abre nem fecha acesso por si. O risco que resta é o do provider distribuído quando entrar: cache com token precisa de cifra e de rede protegida, e isso vira requisito da adoção, não detalhe dela.

---

## ADR-0036: Leitura e sincronização de grupos do diretório federado

**Status:** Aceita
**Data:** 2026-09-27

### Contexto

A ADR-0026 federa **só a autenticação**: o Entra ID prova identidade no login, mas o SecureGate nunca lê o diretório do cliente. As issues [#27](https://github.com/rafsecco/secco-platform/issues/27) e [#28](https://github.com/rafsecco/secco-platform/issues/28) pedem o próximo passo — listar os grupos do diretório federado de um tenant, e depois mapear grupo → perfil, reconciliado no login — para que empresas que já organizam setores como grupos do Entra ID não precisem recriar cada um como perfil à mão.

Isso é uma ampliação de confiança, não uma extensão trivial: a app registration multi-tenant da ADR-0026 tem hoje consentimento **delegado** (`openid profile email`, concedido pelo próprio usuário no login) para provar identidade. Ler o diretório de outra empresa exige permissão de **aplicação** (`GroupMember.Read.All`), concedida pelo **admin do diretório do cliente** — o app passa a poder ler dado do diretório inteiro, não só autenticar quem já está logando. As duas issues pedem que isso fique **opt-in**, e a #28 acrescenta uma exceção explícita à regra "o AD nunca decide quem tem acesso" da ADR-0026, que esta ADR precisa reconciliar sem revogar aquela.

Alternativas avaliadas para o cliente HTTP:

- **SDK `Microsoft.Graph`** — cobre toda a API, mas é uma superfície grande (dezenas de pacotes transitivos) para duas operações (listar grupos, listar membros de um grupo). Descartado: mais dependência para auditar por menos uso.
- **MSAL (`Microsoft.Identity.Client`)** para obter o token de aplicação — resolve cache de token e retry, mas o fluxo client-credentials contra um endpoint de token é o mesmo padrão que `Secco.SDK.ClientCredentials` já implementa à mão para os próprios clients OIDC da plataforma. Descartado: dependência nova para reimplementar o que já existe.
- **Cliente HTTP mínimo, sem SDK** — `HttpClient` direto contra `/{directoryId}/oauth2/v2.0/token` (client-credentials, escopo `https://graph.microsoft.com/.default`) e contra `https://graph.microsoft.com/v1.0/...`, com paginação por `@odata.nextLink`. **Escolhida.**

### Decisão

**Mesma app registration, consentimento novo e opt-in.** Nenhuma app nova: a app multi-tenant da ADR-0026 ganha o pedido de permissão de aplicação `GroupMember.Read.All`, mas cada empresa cliente só concede quando o próprio admin fizer o *admin consent* daquele escopo — a plataforma nunca sabe de antemão se um tenant concedeu. Sem consentimento, a chamada ao Graph falha com `Authorization_RequestDenied`, e a API traduz isso para um erro claro ("federação sem leitura de diretório — peça ao admin do cliente para conceder consentimento"), nunca um 500 genérico. Federação desabilitada ou tenant sem `TenantFederation` responde 404 antes de tentar qualquer chamada.

**Cliente Graph próprio, sem SDK novo.** Um `IEntraGroupDirectory` na Application, implementado na Infrastructure com `HttpClient` puro:

- Token de aplicação por `client_credentials` contra `/{directoryId}/oauth2/v2.0/token`, usando o `ClientId`/`ClientSecret` já configurados em `SecureGate:EntraId` (ADR-0026) — cache em memória por `directoryId` até pouco antes do `expires_in` (classe 1 da ADR-0035, TTL curto).
- Listagem por `GET /v1.0/groups?$filter=...&$top=...`, paginação pelo `@odata.nextLink` da própria resposta, exposta ao chamador como um **token opaco de página** — não o número de página que o resto da plataforma usa (`PageRequest`/`PagedResult<T>`), porque a paginação do Graph é por cursor, não por offset. Divergência assumida: esta é a primeira leitura cross-produto que não cabe na paginação padrão.
- **Filtro de busca por nome sanitizado antes de entrar no `$filter` OData**: aspa simples duplicada e caractere de controle removido — é o equivalente a injeção de SQL para OData, e a única barreira é validar antes de concatenar (ADR-0020, mesmo princípio da allowlist de identificador na ADR-0028).

**Casamento por id do grupo, nunca pelo claim `groups` do token.** O Entra omite o claim `groups` do id_token acima de um limite de grupos por usuário, e não resolve grupo aninhado — qualquer leitura de "a que grupos a pessoa pertence" é sempre uma chamada à Graph (`GET /users/{oid}/transitiveMemberOf`, filtrada a grupos de segurança), nunca o token. Mapeamento grupo→perfil casa pelo **id do grupo** (imutável), nunca pelo nome (renomeável).

**Mapeamento explícito, por tenant, gerido pelo admin.** Nova entidade `TenantGroupRoleMapping` (`tb_tenant_group_role_mappings`): `TenantId`, `EntraGroupId`, `EntraGroupDisplayName` (snapshot legível, não é chave), `RoleId`. Gestão via API sob `securegate:admin`, no mesmo padrão das demais entidades de tenant. Perfil reservado (ADR-0023/0024/0031) nunca é mapeável — mesma allowlist de `RoleInputRules.IsAssignableToUsers`.

**Origem da atribuição, sobre o vínculo já existente.** Em vez de uma tabela lateral, `UserRole` (hoje `sealed class UserRole : IdentityUserRole<Guid>` sem colunas extras) ganha `Origin` (`Manual` | `Directory`) e `SourceGroupId` (nullable). Continua sendo o vínculo nativo do Identity — "perfil = Role nativo" (issue #26) não muda —, só ganha metadado de como a atribuição chegou lá. `GetUser` e `RoleMemberDto` expõem a origem, pedido explícito do comentário da #28. **`RemoveUserRole` sobre atribuição de origem `Directory` recusa com erro explícito** ("esta atribuição vem da sincronização de grupo; ajuste o mapeamento ou aguarde a mudança no diretório"), em vez de remover e deixar o próximo ciclo reconciliar de volta — recusar é o que torna a origem útil, como o próprio comentário da issue nota.

**Reconciliação: no login E por job periódico, nunca só no login.** No login federado (`EntraSignInProcessor`), depois de autenticar, resolve os grupos do usuário e reconcilia os perfis mapeados daquele tenant. Sozinho isso deixaria quem sai do grupo com o perfil até o próximo login — inaceitável com refresh token de longa duração (ADR-0022/0032). Um **job diário via `IBackgroundJobScheduler`** (Hangfire, ADR-0015 Camada 2, já existente no SDK) itera os tenants com mapeamento configurado, um `GET /groups/{id}/members` por grupo mapeado (mais barato que um `GET .../memberOf` por usuário), e reconcilia contra os usuários pré-provisionados.

**Fail-closed, sem exceção.** Falha ao ler o Graph (indisponível, consentimento revogado, rate limit) **não concede nem remove nenhum perfil** — mantém o estado anterior e registra o motivo na auditoria. É a mesma postura fail-closed da ADR-0021 para o cache de permissão.

**Guarda do último membro ativo, generalizada.** A reconciliação **nunca retira** um perfil de um usuário se isso deixasse o perfil sem nenhum membro ativo — mesmo princípio do "último operador ativo" da ADR-0030 (`OperatorGuard`), mas aplicado a qualquer perfil, não só o de instalação: o secco-intranet exige isto para `intranet-admin`, e generalizar custa pouco e evita reinventar a regra por adotante. O pior efeito possível é alguém manter acesso por mais tempo do que deveria — nunca a instalação travar.

### Consequências

- **Sem migration de dado** além das colunas/tabela novas: `tb_tenant_group_role_mappings` (nova) e `Origin`/`SourceGroupId` em `tb_user_roles` (nullable/default `Manual`, retrocompatível com toda atribuição existente).
- **Nenhum pacote novo no monorepo** — sem `Microsoft.Graph`, sem `Microsoft.Identity.Client`. O custo é reimplementar paginação e obtenção de token à mão; o ganho é zero superfície de dependência nova para auditar.
- **A app registration multi-tenant ganha um segundo tipo de consentimento** (aplicação, além de delegado) — precisa entrar na documentação de deploy do adotante (ADR-0026 já pede a criação da app; esta ADR acrescenta o pedido do escopo `GroupMember.Read.All`).
- **SecureGate ganha uma dependência de runtime nova de fato**: chamadas de saída à Microsoft Graph, sujeitas a indisponibilidade e rate limit do lado do Microsoft 365 do cliente — tratadas como qualquer dependência externa (timeout, sem retry automático que amplifique rate limit, erro traduzido nunca vazando detalhe de terceiro ao chamador, ADR-0020).
- **#27 entra primeiro** (só leitura, menor risco); **#28 depende desta ADR ratificada** antes de qualquer código, dado o tamanho da superfície.
- **O SecureGate continua o único dono do modelo de perfil** (Role/Permission) — o Entra nunca define permissão, só informa quem está em qual grupo. O que muda é *quem entra e sai* de um perfil específico, e só depois de um ato explícito do admin do SecureGate: criar o mapeamento grupo→perfil. A partir desse mapeamento, quem administra a lista de membros daquele grupo **no diretório do cliente** — o TI da empresa, ou um dono de grupo delegado que talvez nem seja admin — passa a decidir indiretamente quem ganha aquele perfil, sem tocar no SecureGate e sem saber que o perfil existe. Não é falha de desenho: é o que a #28 pede. O controle que sobra do lado da plataforma é o admin escolher **quais perfis aceitam essa delegação** (criando ou não o mapeamento) e poder desfazê-la a qualquer momento removendo-o.
- Validação real de consentimento e paginação do Graph exige um tenant Entra de teste — não coberta por unit/integration test contra um Graph falso, que valida o formato da chamada, não o comportamento real do serviço.

---

## ADR-0037: Clients OAuth — de produto vinculados a tenant, de plataforma por configuração

**Status:** Aceita
**Data:** 2026-10-08

### Contexto

A issue [#31](https://github.com/rafsecco/secco-platform/issues/31) pede que a empresa adotante registre, pela API, o client `client_credentials` de um sistema seu (o "Sistema de compras" do `secco-intranet`) e possa entregar o secret a outro time. Hoje isso é impossível, e a análise mostrou dois problemas maiores que a issue:

1. **O token de máquina não tem tenant, e quem tem o secret escolhe o tenant.** `HandleClientCredentialsAsync` emite sem `tenant_id`; o `TenantResolver` aceita então o `X-Tenant-Id` sozinho. A autorização não contém o cruzamento: os papéis do client são strings globais (`ds_roles`) resolvidas como `(tenant alvo, nome do papel)`, e um papel homônimo com escrita no tenant alvo vira escrita cross-tenant — a mesma colisão que a ADR-0031 fechou no token elevado. A ADR-0024 justificou o caminho "sem claim → header" com a premissa de que tokens sem `tenant_id` "só são emitidos a identidades privilegiadas"; uma credencial entregue a outro time quebraria essa premissa.
2. **Nenhum client nasce em produção.** Todos — os do LogStream lendo catálogo, o do AdminPortal, o `installation-auditor` (ADR-0031) — são criados pelo `SecureGateDevelopmentDataSeeder`, que por construção (ADR-0019) nunca roda fora de DEV. Instalar a plataforma de verdade exige insert manual nas tabelas do OpenIddict.

Dois escopos existentes atravessam tenant por construção e não podem ser entregues a uma credencial de produto: `catalog:<produto>` (`ListCatalogTenants` devolve as connection strings de **todos** os tenants) e `authorization:read` (o tenant viaja no path).

Alternativas avaliadas:

- **Só a #31, clients de plataforma depois** — deixa a instalação real dependente de insert manual por mais uma entrega. Descartada pelo dono do produto.
- **Uma API única para os dois tipos, com campo "sem tenant"** — quem tem `securegate:admin` passaria a emitir identidade cross-tenant por chamada HTTP. Descartada: superfície grande demais.
- **Clients de plataforma por bootstrap único** (secret exibido uma vez no console) — sem fonte da verdade, estado deriva em silêncio, rotação precisa de outra ferramenta, secret no log do container. Descartada.
- **Clients de plataforma por seed de referência que só cria** — remover da configuração não desliga. Descartada.
- **Rotação com sobreposição de secrets** — durante a janela, o secret vazado segue valendo, e exige validação fora do fluxo nativo do OpenIddict. Descartada até haver demanda de rotação sem indisponibilidade.

### Decisão

**Dois tipos de client, distinguidos no dado.** `OidcApplication` ganha `TenantId` (anulável) e `Origin` (`Api` | `Configuration`). Client **de produto**: tem tenant, nasce pela API. Client **de plataforma**: sem tenant, nasce pela configuração. Cada caminho só enxerga e altera os seus.

**Client de produto — token com `tenant_id`.** O token de `client_credentials` de um client vinculado sai com a claim `tenant_id` do vínculo. A regra de conflito existente do `TenantResolver` (claim ≠ header → 400) passa a conter o client no próprio tenant, e as permissões são resolvidas por `(tenant vinculado, papel)`. **SDK e produtos não mudam.** Na emissão, o SecureGate re-checa que o tenant está ativo (desativar tenant precisa parar as máquinas dele, não só as pessoas) e que todo escopo pertence à lista de produto (defesa contra edição direta do banco).

**Gestão por API**, sob `/api/v1/tenants/{tenantId}/clients` e `securegate:admin`: registrar, listar, detalhar, alterar acesso (`PUT` idempotente com nome, escopos e papéis), rotacionar secret e revogar. O `clientId` é gerado pelo servidor (`cli_` + aleatório), o secret também, exibido uma única vez, persistido só como hash. Client de outro tenant ou de plataforma responde `404`. A API **não tem caminho** para client sem tenant.

**Escopos de produto: lista fechada no código.** Só escopos de API de produto (`logstream`, `notificationhub`); um produto novo entra na lista por mudança de código. `securegate:admin`, `authorization:read`, `catalog:*` e `securegate` são **escopos de infraestrutura**, exclusivos de client de plataforma.

**Papéis do client de produto: perfis existentes do tenant**, os mesmos que a gestão de perfis (issue #26) administra para pessoas — máquina e pessoa no mesmo modelo (ADR-0021). Papel reservado é recusado. `DeleteRole` passa a contar o client que usa o perfil como membro: excluir perfil em uso por máquina responde `409`, e um perfil recriado com o mesmo nome nunca devolve acesso a um client esquecido.

**Rotação imediata e revogação por remoção.** Rotacionar troca o secret na hora; revogar remove o client. Como o token de máquina não carrega `sver` (ADR-0032), tokens já emitidos valem até expirar — no máximo o TTL do access token (5 minutos por padrão).

**Client de plataforma — reconciliação declarativa na subida.** A seção `SecureGate:PlatformClients` declara cada client (`ClientId`, `Type` = `ClientCredentials` | `AuthorizationCode`, escopos, papéis, redirect URIs); o secret vem de variável de ambiente ou cofre, nunca de arquivo versionado. Na subida, o SecureGate cria ou atualiza cada client declarado e **remove** client de origem `Configuration` que deixou de estar declarado. A configuração é a fonte da verdade; trocar o secret nela é a rotação. Configuração inválida derruba o startup: `ClientId` fora do padrão kebab-case ou com o prefixo `cli_`, tipo desconhecido, authorization code sem redirect URI, redirect URI sem HTTPS fora de Development, secret ausente ou com menos de 32 caracteres fora de Development, escopo não registrado. Client público (sem secret) não existe nesta versão.

**Migração sem camada de compatibilidade.** Todo client existente vira origem `Configuration`; na primeira subida, o que não estiver declarado é removido. O adotante converge declarando seus clients — decisão consciente do dono do produto, registrada como quebra na nota de upgrade. Em DEV, os clients saem do código do seeder de desenvolvimento para o `appsettings.Development.json` do SecureGate, que passa a exercitar o caminho de produção a cada execução local.

**Emenda à ADR-0024.** A frase "tokens sem `tenant_id` só são emitidos a identidades privilegiadas" deixa de ser premissa e vira garantia de construção: pela API só nasce client com tenant, e token sem tenant só sai para client declarado na configuração da instalação — controlada por quem já detém a chave de assinatura e a connection string do SecureGate.

### Consequências

- **A instalação real deixa de depender de insert manual**: o mínimo para subir SecureGate + produtos + AdminPortal em produção passa a ser configuração.
- **Quem pode criar identidade cross-tenant fica explícito**: só quem altera a configuração do SecureGate. Não amplia o que essa pessoa já pode (ela já tem a chave de assinatura), e tira esse poder de quem só tem `securegate:admin`.
- **Sistema da empresa que seja ele mesmo resource server** (usa `AddSeccoAuthorization()` e, portanto, `authorization:read`) ou multi-tenant com catálogo próprio é **serviço de plataforma**: entra pela configuração do operador da instalação, não pela API. A API cobre o caso da issue — sistema que **consome** os produtos da plataforma.
- **Até 5 minutos de token vivo** após rotacionar ou revogar, igual a todo token de máquina hoje (ADR-0032).
- **Quebra para o adotante**: clients inseridos à mão desaparecem na primeira subida até serem declarados. Aceito, com nota de upgrade.
- **Bateria negativa provada por mutação** é parte da decisão, como na ADR-0031: header divergente recusado no produto; papel homônimo em outro tenant não concede nada; escopos de infraestrutura recusados no registro e na emissão; tenant desativado sem token; secret antigo morto na rotação; client revogado sem token; client de plataforma invisível pela API; perfil em uso por client não excluível; reconciliação não toca client da API; configuração inválida derruba o startup.
- **Fora desta ADR**: tela no AdminPortal; client público (SPA/mobile); rotação com sobreposição; vínculo de um client a mais de um tenant.

**Emenda (2026-10-08, antes de qualquer código).** Onde a decisão diz "na subida", leia-se **"no seed de referência"**: a reconciliação dos clients de plataforma é um passo do seed de referência (ADR-0019), logo depois do registro dos escopos, e roda onde ele roda — automática em Development, no processo controlado fora dele (ADR-0005), como a convergência de connection strings da ADR-0025. Rodar no startup de produção reabriria a corrida entre réplicas que fez o seed de referência sair do boot. A **validação** de `SecureGate:PlatformClients` continua fail-fast em toda subida, em qualquer ambiente. Achado no caminho: fora de Development nenhum código chama o seed de referência hoje — nem os escopos existem numa instalação real sem execução manual; o caminho de produção desse processo é lacuna própria, fora desta ADR.

---

## ADR-0038: Migrations e seed de referência fora de Development

**Status:** Aceita
**Data:** 2026-10-08

### Contexto

A ADR-0005 manda aplicar migrations "via processo controlado (não no startup em produção)", e a ADR-0019 manda o seed de referência rodar "em TODOS os ambientes". O processo controlado nunca foi construído: em todos os produtos e no template, `Migrate*Async` e `SeedSeccoDataAsync` só são chamados dentro de `if (IsDevelopment())` (issue [#34](https://github.com/rafsecco/secco-platform/issues/34)). Numa instalação real, sem execução manual, o banco do SecureGate fica sem escopos, sem tenant de plataforma, sem papel de operador e — desde a ADR-0037 — sem clients de plataforma.

Há um segundo buraco que a issue não nomeava. O SecureGate provisiona banco de tenant em tempo de execução (ADR-0028), e a ADR-0028 deixa a migração com cada produto. Hoje o produto só migra os tenants do catálogo ao subir em DEV: um tenant criado depois do deploy fica com o banco vazio até alguém reiniciar o produto.

Um fato muda o peso de uma decisão anterior: o EF Core 10 adquire um **lock exclusivo** ao aplicar migrations. A corrida entre réplicas que tirou o seed do boot (pergunta 431 do log de design) não existe mais nas migrations — mas continua existindo no seed de referência, que não tem lock e, desde a ADR-0037, remove clients.

Alternativas avaliadas:

- **Migrar e semear no startup, em todo ambiente, apoiado no lock do EF.** Automático, mas obriga a credencial de runtime a ter DDL sempre, torna o boot proporcional ao número de tenants, deixa um tenant problemático bloquear a subida inteira e mantém o seed sem lock. Descartada.
- **Bundle de migrations do EF mais comando de seed separado.** Dois artefatos por produto e por engine, e o seed precisa do código da aplicação de qualquer forma. Descartada.
- **Para o tenant novo: job periódico no produto, passo manual (`migrate --tenant`) ou aviso do SecureGate ao produto.** O job deixa janela em que o tenant existe e não funciona; o passo manual quebra o fluxo de criar tenant pela Intranet; o aviso faria o SecureGate depender de todos os produtos e daria a cada produto um endpoint que executa DDL. Descartadas.

### Decisão

**O processo controlado é um verbo do próprio binário.** `dotnet Secco.<Produto>.Api.dll migrate` aplica as migrations e, só se todas passarem, o seed de referência — e sai, sem subir o servidor. O deploy o executa antes das réplicas (passo de pipeline, job ou *init container*). O SDK (`Secco.SDK.EntityFrameworkCore`) define a porta `ISeccoDatabaseMigrator`, que cada produto implementa (banco de plataforma, ou todos os tenants do catálogo), e a rotina única `RunSeccoMigrationsAsync`, usada pelo comando e pelo startup em Development — o F5 continua automático, pelo mesmo código. Num produto com tenants, a falha de um tenant não interrompe os outros; o comando sai com código diferente de zero e lista os que falharam, sem connection string. O template `secco-service` gera o mesmo padrão.

**O tenant novo migra no primeiro uso.** Um `DbConnectionInterceptor` do SDK, registrado só nos contextos de **tenant**, confere e aplica as migrations pendentes na primeira abertura de cada banco no processo, guardando em memória (pelo hash da connection string, nunca o texto) os bancos já conferidos. Aberturas concorrentes aguardam a mesma tarefa; o lock do EF cobre as outras réplicas. Falha não é memorizada — a próxima abertura tenta de novo — e vira `TenantDatabaseUnavailableException`, que o SDK traduz em `503` com `Retry-After`, afetando só aquele tenant. Cobre HTTP, workers e jobs do Hangfire, porque intercepta a conexão e não a requisição. Nenhum privilégio novo: a credencial de runtime do tenant já tem `db_owner` no próprio banco (ADR-0028).

**O seed de referência continua fora do boot de produção.** Ele não tem lock, e a ADR-0037 o tornou destrutivo para clients de plataforma. Por isso o startup do SecureGate fora de Development faz o oposto de migrar: **recusa-se a subir** se o banco de plataforma tiver migration pendente, e manda rodar `migrate`. É checagem só de leitura.

**Base comum para falha transitória.** `Secco.SharedKernel` ganha `SeccoTransientException` (abstrata, deriva de `SeccoException`): falha de infraestrutura em que o chamador pode tentar de novo. `TenantCatalogUnavailableException` (SDK AspNetCore) passa a derivar dela, e `TenantDatabaseUnavailableException` (SDK EF Core) nasce derivando dela; o middleware de tenancy traduz a base em `503`. Admitida pela ADR-0003: é exceção base, usada pelos dois pacotes do SDK, sem dependência e sem estado — e é o único jeito de os dois pacotes, que não se referenciam, compartilharem o contrato.

### Consequências

- O "processo controlado" da ADR-0005 passa a existir; a ADR-0019 ("seed de referência em todos os ambientes") deixa de depender de execução manual.
- A dependência da ADR-0037 em relação à #34 se resolve: os clients de plataforma passam a nascer numa instalação real pelo `migrate`.
- O deploy ganha um passo obrigatório. Esquecê-lo no SecureGate é barrado no startup; nos produtos com tenants, o primeiro uso migra o que faltar.
- A primeira requisição de um tenant novo paga a migração; as seguintes não pagam nada. Por processo, cada tenant já migrado custa uma leitura da tabela de histórico.
- O `docker-compose.yml` ganha um serviço `*-migrate` por produto, do qual a API depende; o job `docker-stacks` do CI passa a provar o comando real dentro do container real a cada push.
- **Fora desta ADR, registrado:** criar o banco (`CREATE DATABASE`) — é provisionamento: tenants pela ADR-0028, bancos de plataforma (SecureGate, Hangfire) pelo DBA; seed de referência **por tenant** — previsto pela ADR-0019 e sem mecanismo, nenhum produto tem hoje; expand/contract de schema em deploy com réplicas de versões diferentes.

---

## ADR-0039: Catálogo de permissões publicado por produto

**Status:** Aceita
**Data:** 2026-10-10

### Contexto

A issue [#33](https://github.com/rafsecco/secco-platform/issues/33) (`adopter-demand`) aponta que ninguém fora do processo de um produto sabe quais permissões ele define. O SecureGate guarda e resolve permissões de perfil (ADR-0021), mas o `SetRolePermissions` só valida o formato `recurso:acao` (`SeccoPermissions`). Cada produto mantém o próprio vocabulário em constantes no código (`LogStreamPermissions`, `NotificationHubPermissions`, `IntranetPermissoes` no adotante). Quem administra perfis de um sistema que não é o seu — a Intranet gerindo o "Sistema de compras" (ADR-0030), ou o AdminPortal — digita de memória, e um `pedido:aprovra` é aceito, salvo e simplesmente não funciona. A pergunta 82 do log de design deixou registrado o gatilho: "um seletor com catálogo entra se houver demanda real".

O nome de permissão não carrega o produto: `documentos:read` de dois produtos é a mesma string, já hoje, na autorização.

Alternativas avaliadas:

- **Publicar na subida de cada réplica.** Efeito colateral no boot, N réplicas publicando o mesmo, e retentativa em segundo plano com o SecureGate fora. Contraria a ADR-0038. Descartada.
- **O SecureGate busca o catálogo em cada produto** (`/.well-known/permissions`). O SecureGate passaria a depender de todos os produtos e de conhecer a URL de cada um — direção já descartada na ADR-0028. Descartada.
- **Aceitar permissão desconhecida e devolver aviso.** Muda o `PUT` de `204` para corpo, mudando a assinatura no client, e aviso que ninguém lê não protege. Descartada.
- **Recusa ligada por configuração, desligada por padrão.** O padrão continuaria aceitando o erro de digitação. Descartada.
- **Publicar remove a permissão órfã de todos os perfis.** Escrita destrutiva em todos os tenants disparada pelo deploy de um produto; uma permissão removida por engano apagaria atribuições que voltar a versão não devolve. Descartada.
- **Recusar também a órfã já gravada.** Obrigaria limpeza não relacionada a cada edição de perfil. Descartada.

### Decisão

**O catálogo é por produto, e o produto o publica no `migrate`.** Cada produto declara, na camada Application, a lista de `SeccoPermissionDefinition(Name, Description)` — record novo no SharedKernel, usado por dois ou mais produtos, sem dependência nem estado (ADR-0003). No `migrate` (ADR-0038), depois das migrations, um seeder de referência publica a lista inteira no SecureGate por `PUT /api/v1/permission-catalog/{product}`, idempotente. O catálogo só muda quando o código muda, ou seja, no deploy. O `Secco.SecureGate.Client` entrega o publicador; o SDK EF Core entrega um seeder de referência genérico por delegate; o produto liga os dois na Infrastructure — assim o client não arrasta o EF Core para quem só consome a API. Sem a seção `Secco:SecureGate`, o passo é pulado. Com ela, uma falha de publicação falha o `migrate` e para o deploy do produto: sem catálogo, as permissões novas daquela versão ficariam impossíveis de atribuir.

**Autorização da publicação por produto.** Escopo `permissions:<produto>`, conferido contra o produto da rota — mesma mecânica do `catalog:<produto>`. O client do LogStream publica só o catálogo do LogStream. `permissions:*` é escopo de infraestrutura (ADR-0037): só client de plataforma, nunca client de produto. A leitura do catálogo exige `securegate:admin`.

**A publicação nunca toca perfil.** Ela substitui apenas as entradas daquele produto.

**`SetRolePermissions` recusa o acrescentado desconhecido.** Uma permissão que o perfil ainda não tinha só é aceita se aparecer no catálogo de **algum** produto; senão, `400` listando as desconhecidas. As permissões que o perfil já tinha passam, mesmo órfãs — o admin pode editar outra coisa sem ser obrigado a limpar naquele momento.

**A órfã fica visível e só sai por ação do admin.** O detalhe do perfil passa a informar `unknownPermissions`: as gravadas que nenhum catálogo declara. Órfã não concede nada — nenhum produto a confere.

**Limpeza de órfãs é ato explícito, por tenant.** `POST /api/v1/tenants/{tenantId}/roles/remove-unknown-permissions` (`securegate:admin`) tira de todos os perfis **daquele** tenant as permissões que nenhum catálogo declara — nunca cross-tenant numa chamada, pelo mesmo motivo que descartou a remoção automática. Idempotente (ADR-0034).

**Colisão de nome entre produtos fica visível, não é proibida.** O nome não ganha namespace de produto nesta ADR: hoje não há colisão, e renomear exigiria reescrever as permissões de todos os perfis de todos os tenants. Em vez disso, a publicação registra no log quando um nome já existe no catálogo de outro produto, e a leitura do catálogo informa, por permissão, os outros produtos que declaram o mesmo nome. Se a colisão acontecer de fato, a decisão volta com evidência.

**O AdminPortal usa o catálogo.** Na página de perfil, o texto livre das permissões vira lista de caixas agrupada por produto, com a descrição; as órfãs aparecem à parte, sinalizadas, e podem ser desmarcadas; há uma página de leitura do catálogo e um botão de limpeza de órfãs do tenant, com confirmação.

### Consequências

- Fecha a porta do erro de digitação na atribuição; a tela de perfis pode trocar o campo livre por uma lista.
- **Deploy de produto passa a exigir o SecureGate no ar** — na prática já exigia, pelo catálogo de tenants e pela autorização.
- **Quebra para o adotante:** permissão nova só é aceita depois que o produto dono publicar o catálogo. A Intranet publica o dela ao adotar o client novo; as permissões que os perfis dela já têm continuam passando.
- Duas definições do mesmo nome em produtos diferentes coexistem no catálogo (uma por produto); a validação usa a união.
- **Namespace de produto no nome da permissão fica para quando houver colisão real** — o catálogo a torna visível. Alternativa descartada junto: recusar colisão na publicação (`409`) impediria um adotante de publicar por causa de um nome escolhido antes por outro produto.

---

## ADR-0040: Pessoa membro de vários tenants — o tenant do login vem da aplicação

**Status:** Aceita
**Data:** 2026-10-15

### Contexto

A ADR-0022 decidiu que "o registro do usuário carrega o tenant", e a entrega 6.5 fez o login com e-mail único na instalação e tenant tirado do registro. Uma pessoa pertence a **um** tenant. A ADR-0030 deu o motivo de a instalação ter vários tenants: a empresa que adota roda vários sistemas seus sobre o mesmo SecureGate. Juntas, as duas decisões se contradizem assim que a empresa tem o segundo sistema: o funcionário que usa a Intranet (tenant A) e o Sistema de compras (tenant B) não consegue conta nos dois, porque o e-mail já está em uso, e o SSO entre os sistemas — um dos ganhos da tenancy — não existe (issue [#35](https://github.com/rafsecco/secco-platform/issues/35)).

O dono do produto escolheu a direção na conversa de design da #32 e pediu uma prova antes da ADR. O spike (branch `spike/35-varios-tenants`, descartável) confirmou o comportamento, no teste de integração e no navegador: a mesma conta entrou na Intranet com senha e no Compras **sem tela**; cada aplicação recebeu o token com o próprio `tenant_id` e só os perfis daquele tenant; o RH, sem vínculo, recebeu `access_denied` sem derrubar a sessão nas outras; removido o vínculo do Compras, a renovação dele parou e a da Intranet continuou. A suíte do SecureGate passou inteira com a mudança (772 testes): clients sem vínculo seguem como hoje.

O spike também achou o que muda de natureza com vários vínculos:

1. **Perfis precisam ser filtrados pelo tenant do login.** Hoje o login lê todos os perfis da conta (`GetRolesAsync`); com dois vínculos, o token do Compras levaria os perfis da Intranet, e o resolvedor consultaria `(B, perfil-de-A)` — a colisão de nome que a ADR-0031 e a ADR-0037 já fecharam em outros caminhos.
2. **"Ser operador" precisa olhar o tenant do login**, não só o de origem da conta: um operador membro do Compras, com um perfil homônimo de `installation-operator` lá, sairia com token sem `tenant_id` e com `securegate:admin`.
3. **Operações sobre a credencial atingem todos os tenants da pessoa.** Quem reseta a senha ou o segundo fator de alguém, troca o e-mail ou desvincula o login do Entra assume a conta dela em todos os sistemas. Com um tenant por pessoa isso não tinha como vazar; com vários, passa a ter.
4. O client interativo vinculado a tenant esbarra na restrição da ADR-0037 (só client de origem `Api` tem tenant), e a ADR-0037 só prevê client interativo pela configuração.

Alternativas avaliadas:

- **E-mail único por tenant** (uma conta por sistema). Senhas e segundos fatores separados, sem SSO, e desligar uma pessoa vira desligar N contas. Descartada na conversa de design.
- **Manter como está.** A pessoa só existe num sistema da empresa. Descartada: anula o motivo da tenancy (ADR-0030).
- **Seletor de tenant na tela de login.** A pessoa escolhe o tenant ao entrar. A aplicação já sabe para que tenant serve; perguntar à pessoa abre espaço para entrar num sistema com o tenant de outro, e exige tela nova. Descartada.

### Decisão

**Uma conta por pessoa, membro de vários tenants.** O e-mail continua único na instalação. O vínculo pessoa ↔ tenant passa a ser N:N, numa tabela própria (`tb_tenant_memberships`: pessoa, tenant, ativo, criação), com chave natural `(pessoa, tenant)`. Perfis continuam por tenant (ADR-0021) e só valem para quem tem vínculo ativo naquele tenant.

**Tenant de origem.** A coluna de tenant do usuário continua e passa a significar **tenant dono da conta**: o tenant que a provisionou. Ele tem sempre um vínculo. É dele a federação Entra que autentica a pessoa (ADR-0026), é nele que a elevação se audita (ADR-0031), e é ele que governa a credencial (abaixo). Tenant dono desativado bloqueia a pessoa em todos os sistemas: a conta é dele, e na dúvida o lado seguro é não emitir.

**O tenant do login vem da aplicação.** Client interativo **vinculado a tenant** emite sempre para o tenant do vínculo. Client interativo **sem vínculo** (de plataforma, pela configuração — o AdminPortal) emite para o tenant dono da conta, como hoje. A pessoa não escolhe tenant. Sem vínculo ativo no tenant da aplicação, o `/connect/authorize` responde `access_denied` ao `redirect_uri` daquela aplicação; a sessão da pessoa no SecureGate não é encerrada.

**A sessão é da pessoa; o token é da aplicação.** O cookie do SecureGate, a senha e o segundo fator são um só — é isso que dá o SSO. Cada emissão (code, renovação) re-deriva do cadastro: vínculo ativo, tenant do login ativo, tenant dono ativo, conta não bloqueada, e **perfis só do tenant do login**. O tenant da renovação é o do token que o próprio SecureGate emitiu, nunca um parâmetro da requisição.

**Operador de instalação é decidido pelo tenant do login.** O token sem `tenant_id` e o escopo `securegate:admin` (ADR-0023/0024) exigem que o tenant do login seja o de plataforma **e** que a conta seja dele. Um operador que entra no Compras recebe um token comum do Compras. A exigência de segundo fator do operador (entrega D) segue no login de operador; como o segundo fator é da pessoa, quem o tem cadastrado responde a ele em qualquer login.

**Operações de pessoa × operações de vínculo.**
- **Da pessoa**, só pelo tenant dono: convite e reset de senha, reset do segundo fator, login local liga/desliga, troca de e-mail pelo admin, desvínculo do login externo, nome de exibição pelo admin, desativar/ativar a conta, encerrar sessões. Chamadas por um tenant que não é o dono respondem `404`, como recurso de outro tenant responde hoje.
- **Do vínculo**, por qualquer tenant de que a pessoa é membro: incluir, suspender, reativar e remover o vínculo, e atribuir ou remover perfis do próprio tenant.

Hoje todas essas rotas exigem `securegate:admin`, que é de instalação; a separação vale desde já porque é o contrato que a área administrativa por tenant (ADR-0030, hierarquia admin de instalação × admin de tenant) vai herdar.

**Fim de acesso por tenant é suspender o vínculo.** Suspender ou remover vínculo move o `SecurityStamp`, como a remoção de perfil já faz (ADR-0032): os tokens em uso da pessoa morrem em todas as aplicações e as que ela ainda pode usar renovam no próximo login silencioso (SSO). É mais amplo que o necessário, e é o mecanismo de revogação que já existe e já tem teste — revogar só os tokens de um tenant exigiria `sver` por vínculo, que fica para quando houver demanda.

**Gestão por API**, sob `securegate:admin` (ADR-0034):
- `PUT /api/v1/tenants/{tenantId}/members/{userId}` inclui ou reativa, idempotente; `POST .../members/{userId}/suspend` suspende; `DELETE .../members/{userId}` remove (vínculo do tenant dono não se remove: `409`).
- A pessoa é localizada por e-mail na própria inclusão (`POST /api/v1/tenants/{tenantId}/members` com `email`: `404` se não existir, `409` se já for membro).
- `GET /api/v1/tenants/{tenantId}/users` passa a listar dono e membros, com a indicação de qual é cada um.
- Atribuir perfil a quem não é membro do tenant do perfil responde `409`.

**Client interativo vinculado a tenant nasce pela API do tenant** (extensão da ADR-0037). `/api/v1/tenants/{tenantId}/clients` passa a aceitar o tipo `authorization_code`: client confidencial, PKCE obrigatório, redirect URIs HTTPS (fora de Development), escopos de produto mais os OIDC padrão (`openid`, `profile`, `email`, `roles`, `offline_access`). A restrição "só origem `Api` tem tenant" continua valendo. Client público segue fora (ADR-0037).

**Migração:** cada usuário existente ganha o vínculo com o próprio tenant, na migration. Nada muda para quem tem um tenant só.

### Consequências

- **SSO entre os sistemas da empresa**, uma senha e um segundo fator por pessoa. Desligar a pessoa é uma operação, no tenant dono.
- **Quem usa o SecureGate para um sistema só não muda nada.** O vínculo com o tenant dono nasce junto com a conta, e a aplicação — pela configuração, sem vínculo, ou pela API, vinculada ao único tenant — emite para o mesmo tenant de hoje, com o mesmo token. O custo é uma tabela e uma consulta a mais por emissão. Crescer para o segundo sistema é criar o tenant e incluir as pessoas, sem migração.
- **A invariante mais fácil de quebrar é o filtro de perfis pelo tenant do login**: todo caminho que hoje lê os perfis da conta inteira passa a filtrar pelo tenant, e a bateria negativa é provada por mutação, como na ADR-0031 e na ADR-0037: perfil de outro tenant fora do token; homônimo do perfil de operador sem escalar; vínculo ausente, suspenso ou removido sem token; tenant do login desativado sem token; tenant dono desativado sem token em lugar nenhum; renovação mantém o tenant emitido; operação de pessoa por tenant não dono responde `404`.
- **Suspender um vínculo derruba os tokens da pessoa em todos os sistemas** até o próximo login silencioso. Para a pessoa é uma renovação; para quem integra é igual à remoção de perfil hoje.
- **A Intranet tem dois clients interativos**: o do tenant dela, que todo funcionário usa, e um de plataforma, sem vínculo, para a área administrativa on-behalf-of do operador. Isso coincide com a recomendação de segurança registrada na #35 — a área administrativa deixa de usar o client de máquina com `securegate:admin`.
- **A federação Entra continua por tenant dono**: quem é dono no tenant A e membro no B entra pelo diretório de A em qualquer sistema. Um diretório por vínculo fica fora.
- A **#32** (isolamento em três camadas) passa a isolar por vínculo, não pela coluna de tenant do usuário — por isso vem depois desta.
- `Secco.SecureGate.Client` ganha os métodos de vínculo e o tipo de client interativo: versão minor, sem quebra. O token não muda de formato: produtos e SDK não mudam.
- **Emenda à ADR-0022:** "o registro do usuário carrega o tenant" passa a "o registro carrega o tenant dono; o tenant do login vem da aplicação". **Emenda à ADR-0037:** client de produto pode ser interativo.
- **Fora desta ADR:** tela de vínculos no AdminPortal; seletor de tenant para aplicação que sirva a vários tenants; `sver` por vínculo; diretório federado por vínculo; transferir a conta para outro tenant dono.

---

## ADR-0041: Isolamento de tenant no banco do SecureGate — escopo explícito, filtro global e RLS

**Status:** Aceita
**Data:** 2026-10-17

### Contexto

No banco do SecureGate o isolamento entre tenants é convenção: cada consulta escreve `where TenantId == …` à mão — cerca de 45 ocorrências em 8 arquivos da Infrastructure —, sem filtro global no EF nem Row-Level Security no banco (issue [#32](https://github.com/rafsecco/secco-platform/issues/32)). Uma consulta nova que esqueça o filtro vaza perfil, vínculo ou pessoa de outro tenant, e só um teste específico perceberia. A ADR-0040 tornou o risco concreto: sistemas diferentes da mesma empresa dividem as tabelas, e o filtro de perfis pelo tenant do login virou a invariante mais crítica da emissão de token.

A ADR-0040 também mudou o que é "dado de tenant": a pessoa (`tb_users`) atravessa tenants, então o filtro dela é "dona **ou** membro do tenant da operação", não uma coluna.

Banco do SecureGate por tenant foi descartado na própria issue: conteria o vazamento horizontal, mas login por e-mail, catálogo de connection strings, operador de instalação e elevação são cross-tenant por natureza e exigiriam um banco central de qualquer forma.

Alternativas avaliadas para o mecanismo:

- **Filtro global e `IgnoreQueryFilters()` nos caminhos cross-tenant** (como a issue descreve). O RLS da segunda camada não enxerga `IgnoreQueryFilters()`; seria preciso um segundo mecanismo de modo de sessão, e os dois teriam que andar juntos para sempre. Descartada.
- **Filtro lendo o tenant da rota HTTP.** Implícito: não cobre seed nem job, e um endpoint com outro formato de rota fica sem filtro sem ninguém perceber. Descartada.
- **Entregar as três camadas de uma vez.** O RLS só contém injeção se a credencial do runtime não for dona do banco — no SQL Server, `db_owner` desliga a política com um comando injetado; no Postgres, o dono da tabela ignora o RLS sem `FORCE` e o superusuário sempre. Isso separa a credencial do `migrate` da do runtime, uma quebra de implantação que não deveria travar o ganho principal. Descartada em favor de duas rodadas.

### Decisão

**Escopo explícito da operação.** O SecureGate ganha um escopo de acesso por requisição (`ITenantAccessScope`, porta da Application) com três estados: **nenhum**, **tenant X** e **plataforma**. Entrar num estado devolve um `IDisposable` que restaura o anterior, delimitando cada trecho. É a fonte única do tenant das camadas 1 e 2.

**Camada 1 — filtro global no EF (rodada 1).** Filtros nomeados do EF Core restringem, ao estado do escopo, as entidades de tenant: perfis, permissões e atribuições de perfil (pelo tenant do perfil), vínculos, bancos de tenant, federação, mapeamento grupo→perfil, concessões de elevação, e a pessoa (dona ou membro). **Sem escopo, nada aparece** (fail-closed). Na escrita, o `SaveChanges` recusa incluir ou alterar entidade de outro tenant estando em escopo de tenant. Ficam fora, por natureza: a própria tabela de tenants (operação de instalação), dados de plataforma sem tenant (catálogo de permissões, chaves de Data Protection), dados da pessoa lidos só pelo id dela (logins externos, claims, tokens do Identity) e as tabelas do OpenIddict — o pipeline do protocolo consulta o client pelo `client_id` antes de existir tenant, e a API de clients do tenant mantém o filtro explícito.

**Borda.** Rotas sob `/api/v1/tenants/{tenantId}` e `/api/v1/authorization/tenants/{tenantId}` entram no escopo do tenant da rota antes do handler. Caminhos cross-tenant entram em modo **plataforma** explicitamente, com motivo: login e emissão de token (a pessoa é achada em modo plataforma; os perfis são lidos já no escopo do tenant do login), páginas de login e de conta da própria pessoa, lista e criação de tenants, catálogo de bancos, contagem de operadores, versão de sessão, publicação do catálogo de permissões, busca da pessoa por e-mail na inclusão de membro, seeds, `migrate` e verificação de migration na subida.

**Camada 3 — teste de arquitetura (rodada 1).** O build reprova `IgnoreQueryFilters` em qualquer ponto do SecureGate e toda entrada em modo plataforma fora de uma lista versionada (arquivo e motivo por linha). Caminho cross-tenant novo só entra mudando essa lista, que aparece no diff.

**Camada 2 — RLS (rodada 2, spec própria).** O estado do escopo vira contexto de sessão do banco a cada abertura e a cada troca de estado (`sp_set_session_context` no SQL Server, `set_config` no Postgres; o pool limpa o contexto ao devolver a conexão). As políticas espelham os filtros da camada 1, com modo plataforma como valor explícito — sem contexto, nada aparece. A credencial do runtime deixa de ser dona do banco (só leitura e escrita); a do `migrate` faz o DDL. Fora de Development, o SecureGate recusa subir com credencial de runtime dona do banco, como recusa com migration pendente (ADR-0038).

### Consequências

- Consulta nova sem filtro deixa de vazar: na rodada 1 pelo EF, na rodada 2 também por SQL cru e injeção nos caminhos de tenant.
- O filtro de perfis pelo tenant do login (ADR-0040) passa a ser garantido pelo escopo, não só pela consulta que o escreve.
- **Limite assumido:** o modo plataforma enxerga tudo, no EF e no banco. Uma injeção num caminho de modo plataforma (o login, por exemplo) não é contida pelo RLS — segue contida pelo EF parametrizado, e esses caminhos são poucos e listados.
- A rodada 1 não muda contrato nem pacote: o adotante não percebe. A rodada 2 traz nota de upgrade (duas credenciais) e o `docker-compose.yml` passa a criar os dois logins.
- Os filtros manuais existentes continuam onde estão na rodada 1 (redundantes, inofensivos); removê-los é limpeza posterior, não requisito.
- Exportar e excluir os dados de um tenant (desejável na issue) fica fora.

**Emenda (2026-10-17, antes do código da rodada 2).** O contexto de sessão pode ser regravado por qualquer comando da conexão, inclusive um injetado. **No SQL Server**, ele é gravado na abertura com `@read_only = 1`, e nada na conexão o muda até o pool resetá-la; trocar de escopo com a conexão aberta (transação em curso) lança, fail-closed. **No Postgres**, não há contexto somente leitura: o RLS segura o filtro esquecido em SQL cru, mas **não** contém injeção com comandos empilhados — limite registrado aqui e na nota de upgrade; o SQL Server segue o provider padrão (ADR-0018). Descartadas: regravar o contexto a cada comando (injeção o regravaria nos dois engines) e uma credencial de runtime por modo (complexidade alta, ganho só no Postgres). A suíte de testes do SecureGate passa a rodar com a credencial de runtime real, sem posse do banco, para que o RLS seja exercitado em todo teste.

---

## ADR-0042: Tratamento de imagem enviada por usuário — `Secco.SDK.Imaging` com SkiaSharp no próprio processo

**Status:** Aceita
**Data:** 2026-10-17

### Contexto

A Intranet vai aceitar foto do colaborador, e qualquer produto com avatar, logotipo ou anexo de imagem precisa do mesmo tratamento (issue [#29](https://github.com/rafsecco/secco-platform/issues/29)). Esse tratamento é fácil de errar:

- arquivo que finge ser imagem;
- EXIF com localização, um risco de LGPD e não só técnico;
- arquivo pequeno em bytes que, ao decodificar, aloca gigabytes.

A plataforma não tinha SDK de imagem, e o adotante recusou implementar isso dentro do produto.

A biblioteca precisa ser utilizável comercialmente sem cobrança e rodar em Windows, Linux e container, sem `System.Drawing`. Alternativas de biblioteca:

- **ImageSharp:** licença dividida, que cobra de empresa acima de um faturamento. Descartada em 2026-09-27.
- **Decodificação gerenciada própria:** escrever PNG e JPEG em C#, sem código nativo, é trabalho grande e um decodificador novo também tem bugs. Fica como evolução possível, não como ponto de partida.
- **SkiaSharp:** MIT, mantida pela Microsoft/.NET Foundation, decodifica com código nativo (libjpeg-turbo, libpng, libwebp).

Alternativas de onde decodificar, já com o SkiaSharp. O código nativo pode ter falha de memória explorável, e a do libwebp em 2023 (CVE-2023-4863) foi explorada de verdade.

- **No próprio processo, com defesas antes e depois.** O risco residual é uma falha não corrigida no decodificador chegar ao processo do produto, onde estão credenciais e sessões.
- **Em processo separado e descartável.** Contém a falha, mas a SDK deixa de ser só uma biblioteca: um executável auxiliar a empacotar em cada imagem Docker de adotante.

### Decisão

Criaremos o pacote **`Secco.SDK.Imaging`**, que recebe os bytes enviados e devolve imagens normalizadas. O produto não conhece a biblioteca por baixo: o SkiaSharp fica atrás de uma porta **interna**, e nenhum tipo dele aparece na API pública.

**Decodificaremos no próprio processo**, porque o caso que existe é upload de usuário autenticado da própria empresa, e o isolamento só se paga com upload anônimo. As defesas são estas:

- leitura limitada em bytes, sem confiar em `Stream.Length`;
- limite de pixels conferido no **cabeçalho**, antes de alocar o buffer de pixels;
- concorrência limitada;
- cancelamento;
- versão fixada no `Directory.Packages.props` com o `NuGetAudit` quebrando o restore diante de advisory.

O usuário erra no input e recebe `Result` com erro tipado (ADR-0004). O programador erra no uso e recebe `ArgumentException`.

**A saída é codificada do zero a partir dos pixels:** orientação do EXIF aplicada, conversão para sRGB e nenhum metadado do original. A SDK trata só bytes; armazenamento, tenant e autorização são do produto. Entram JPEG, PNG e WebP; saem WebP (padrão) ou JPEG.

Uma chamada produz **várias variantes** (nome livre + lado máximo, sem ampliar) a partir de **uma única** decodificação. Assim, o arquivo não confiável passa pelo decodificador uma vez só.

### Consequências

- **Mais fácil:**
  - avatar, logotipo e anexo de imagem em qualquer produto, com as mesmas defesas;
  - trocar o decodificador mais tarde, sem quebrar o adotante.
- **Mais difícil:**
  - a plataforma passa a ter a primeira dependência nativa fora do runtime, e advisory do Skia ou das bibliotecas que ele embute precisa virar atualização rápida;
  - imagem Alpine (musl) fica fora do v1, porque o asset nativo referenciado é o de glibc.
- **Proibido:**
  - usar a SDK em upload **anônimo** sem ADR nova que reavalie o isolamento em processo separado;
  - aceitar formato novo de entrada sem rever esta ADR;
  - expor tipo do SkiaSharp na API pública.

---

## ADR-0043: Famílias de permissão no catálogo — permissão por instância de recurso

**Status:** Aceita
**Data:** 2026-10-17
**Complementa:** ADR-0039 (não a substitui)

### Contexto

A issue [#36](https://github.com/rafsecco/secco-platform/issues/36) (`adopter-demand`) aponta que o catálogo da ADR-0039 só declara **nomes exatos**, publicados no `migrate`, e que o `SetRolePermissions` recusa qualquer permissão acrescentada fora dele. O `secco-intranet` concede acesso **por instância de recurso**: cada setor criado pela tela ganha `setor-{slug}:read` e `setor-{slug}:write`. Os setores são dados de cada tenant, criados a qualquer momento — não há como listá-los no `migrate`. Contra um SecureGate com a ADR-0039, criar setor na Intranet falha. Qualquer produto que conceda acesso por projeto, unidade ou centro de custo cairia no mesmo problema.

Alternativas avaliadas:

- **O produto republica o catálogo a cada instância criada**, com a união das instâncias de todos os tenants. Corrida entre tenants (uma publicação substitui a outra), varredura de todos os bancos de tenant e escrita de infraestrutura no fluxo de uma tela comum. Descartada (recusada também pelo adotante).
- **Catálogo por tenant.** O produto passaria a publicar dado de tenant no catálogo de plataforma, que é dado de deploy. Descartada.
- **Curinga livre** (`setor-*`, ou placeholder em qualquer posição, inclusive na ação). Casamento ambíguo, sem caso real, e um padrão como `{x}:read` aceitaria qualquer leitura — desfaz a proteção contra erro de digitação que motivou a ADR-0039. Descartada.
- **Trocar o acesso por instância de permissão para nome de perfil.** Desfaz o modelo de permissões do produto. Descartada (recusada pelo adotante).

### Decisão

**Uma entrada do catálogo pode ser uma família:** `<prefixo>-{<param>}:<acao>`, por exemplo `setor-{id}:read`, com descrição como "Ler o setor {id}". Regras de forma, validadas no SharedKernel (`SeccoPermissions.IsValidFamily`):

- **um** placeholder, nome `[a-z]+`, **só no fim do segmento de recurso**;
- **prefixo literal obrigatório**, em kebab-case, terminado em `-` antes do placeholder;
- **ação fixa** — a família nunca varia a ação;
- mesmo limite de tamanho do nome canônico (`SeccoPermissions.MaxLength`).

**Casamento sem expressão regular.** Um nome canônico (`SeccoPermissions.IsValid`) casa com a família quando começa com `<prefixo>-`, termina com `:<acao>` e sobra ao menos um caractere entre os dois, sem `:`. Como o nome já é kebab-case válido, o valor do parâmetro é um ou mais trechos kebab (`setor-recursos-humanos:read` casa). Comparação ordinal; nenhuma regex sobre input externo (sem ReDoS). O SharedKernel entrega `IsValidFamily`, `MatchesFamily` e `FromFamily(familia, valor)` — este último para o produto compor o nome a partir da mesma declaração que publica.

**`SeccoPermissionDefinition` não muda.** O `Name` passa a poder ser uma família; o catálogo do SecureGate a guarda como qualquer entrada (sem migration), e a leitura do catálogo informa `isFamily` por entrada.

**`SetRolePermissions` aceita o acrescentado que casa com um nome exato OU com uma família de algum produto.** O resto da ADR-0039 vale como está: só o acrescentado é conferido, colisão fica visível (a família é comparada pelo padrão literal), publicação nunca toca perfil.

**A família só afeta a validação.** A autorização nos produtos continua comparando a string exata da permissão gravada no perfil (ADR-0021). Família não concede nada a ninguém, e a permissão continua sendo por tenant, no perfil do tenant.

**Órfã de família é responsabilidade do produto.** O SecureGate não conhece as instâncias: um nome que casa com uma família publicada nunca é órfão para ele, nem na leitura do perfil (`unknownPermissions`) nem na limpeza por tenant. Ao excluir a instância, o **produto dono** remove a permissão dos perfis — ele é quem sabe que a instância deixou de existir. Se a família deixar de ser publicada, os nomes voltam a ser órfãos e a limpeza da ADR-0039 os alcança.

**O AdminPortal mostra as famílias.** A página do catálogo lista as famílias como padrão, com a descrição. Na edição do perfil, as permissões gravadas que casam com uma família aparecem agrupadas sob ela (desmarcáveis), e cada família oferece um campo para acrescentar uma instância — validado pelo formato antes do envio; a recusa final continua sendo do SecureGate.

### Consequências

- Fecha a lacuna do adotante: a Intranet publica `setor-{id}:read` e `setor-{id}:write` no catálogo estático e cria setores sem tocar no catálogo.
- **Risco residual aceito:** um erro de digitação na **parte da instância** (`setor-rhh:read`) passa na validação. O produto monta esses nomes a partir das instâncias que ele conhece; quem digita à mão no AdminPortal assume o risco, que o prefixo literal e a ação fixa limitam à família certa.
- `Secco.SharedKernel` ganha só acréscimos (minor); `Secco.SecureGate.Client` ganha o campo `isFamily` na leitura do catálogo (minor).
- **Proibido:** placeholder fora do fim do segmento de recurso, mais de um placeholder, ou família sem prefixo literal — qualquer uma dessas formas exige ADR nova.

---

## ADR-0044: Admin de tenant — autoridade administrativa por concessão, on-behalf-of a pessoa

**Status:** Aceita — congelada pela ADR-0051
**Data:** 2026-10-18
**Complementa:** ADR-0030 (dá mecanismo à hierarquia admin de instalação × admin de tenant) e ADR-0040

### Contexto

A issue [#37](https://github.com/rafsecco/secco-platform/issues/37) (`adopter-demand`) aponta que toda a API administrativa do SecureGate exige `securegate:admin`, escopo **de instalação**, que só sai num token de pessoa quando ela é operador de instalação (ADR-0040). No `secco-intranet`, quem administra os tenants dos outros sistemas da empresa é o `intranet-admin`, pessoa do tenant da Intranet e não operador. Sem alternativa, a Intranet administra com um client de máquina com `securegate:admin`. Quem compromete a Intranet, ou acha um bug de autorização numa tela dela, administra **todos** os tenants da instalação, e a trilha da plataforma registra "a Intranet fez", não a pessoa.

A ADR-0030 descreveu a direção (modelo `cluster-admin`/`namespace-admin`) sem mecanismo. A ADR-0040 separou as operações de pessoa (só pelo tenant dono) das de vínculo (por qualquer tenant de que a pessoa é membro), dizendo que esse é "o contrato que a área administrativa por tenant vai herdar". A ADR-0041 classificou cada rota administrativa como de tenant (escopo do tenant da rota) ou de plataforma.

Alternativas avaliadas:

- **Perfil num tenant concede administração de outros** (ex.: `tenant-admin:compras` como permissão de um perfil da Intranet). Autoridade sobre o tenant B nasceria de um perfil no tenant A, que o admin de A edita: é a escalada por perfil homônimo que a ADR-0031 e a ADR-0040 já fecharam. Descartada.
- **Lista de tenants administrados no token** (claim). O token cresce com os tenants, e revogar só vale quando ele expira. O SecureGate é o próprio servidor de recurso e tem o cadastro à mão. Descartada.
- **Área administrativa só para operador de instalação.** Muda quem administra os sistemas da empresa (recusado pelo adotante). Descartada.
- **Dar `installation-operator` ao `intranet-admin`.** Poder sobre a instalação inteira para uma pessoa de tenant. Descartada.
- **Admin de tenant cria tenant e nomeia outros admins.** A autoridade se expandiria sozinha, e uma conta comprometida se perpetuaria criando admins. Fica fora do v1 (ver Consequências).

### Decisão

**A autoridade é uma concessão explícita, guardada na plataforma.** Tabela `tb_tenant_admin_grants`: pessoa, tenant administrado, `sub` de quem concedeu, data. Uma linha por par (pessoa, tenant): administrar vários tenants é ter várias linhas, sem conceito de grupo. Gerida **só pelo operador de instalação** (`securegate:admin`): `PUT /api/v1/tenants/{tenantId}/admins/{userId}` concede (idempotente, ADR-0034), `DELETE` revoga, `GET /api/v1/tenants/{tenantId}/admins` lista. Regras da concessão:

- o **tenant de plataforma nunca é administrável** por esta autoridade (`409`) — senão autoridade de tenant viraria de instalação;
- a pessoa precisa existir e o tenant precisa existir; a pessoa **não** precisa ser membro do tenant que administra;
- admin de tenant não concede nem revoga concessão, nem a própria.

**Escopo novo `securegate:tenant-admin`, só para client de plataforma.** Concedível apenas por `SecureGate:PlatformClients` (ADR-0037), como o `securegate:admin`; nunca a client de produto ou de tenant — um tenant não fabrica a própria credencial administrativa. No login e em toda renovação o escopo só sai se a pessoa tiver ao menos uma concessão ativa; senão é filtrado em silêncio, como o de operador. **Segundo fator obrigatório** para quem recebe o escopo, no `/connect/authorize` antes do code, como o operador (entrega D). O token não muda de forma: `tenant_id` continua o do login (ADR-0040), e não lista os tenants administrados.

**A autorização é conferida no cadastro a cada requisição.** Uma rota aceita a autoridade de admin de tenant só se estiver marcada como tal; nela, o chamador passa se tiver `securegate:admin` (como hoje) **ou** se tiver `securegate:tenant-admin` **e** existir a concessão (`sub` do token, tenant da rota) **e** a conta e o tenant dono dela continuarem ativos. Revogar a concessão vale na próxima requisição. Recusa responde `403` sem dizer se o tenant existe.

**Negado por padrão, rota por rota.** A marcação é explícita no mapeamento, e um teste de arquitetura confere o conjunto contra uma lista versionada, como a allowlist da ADR-0041. Entram no v1:

- leitura do tenant;
- pessoas do tenant (ADR-0040: operações de pessoa continuam só pelo tenant dono — `404` pelos outros), **exceto sobre conta administrativa** — quem tem concessão de admin de tenant ou é operador (dono no tenant de plataforma): convite, reset de senha e de segundo fator, login local, nome, desvínculo de login externo, ativação e encerramento de sessões respondem `403` a esta autoridade e ficam com o operador. O mesmo vale para o que revoga as sessões da pessoa (suspender ou remover vínculo, remover perfil, ADR-0032). Sem isso, o admin só do tenant A enfraqueceria a conta de um admin de A e de B — zerando o segundo fator dele, por exemplo —, e derrubaria em ciclo as sessões do operador, incluindo-o como membro e suspendendo o vínculo;
- vínculos;
- perfis, permissões dos perfis, limpeza de órfãs, atribuição e remoção de perfil;
- `GET /api/v1/tenant-admin/tenants`, nova: os tenants que o chamador administra.

Ficam **só com o operador**: criar, ativar e desativar tenant; connection string e provisionamento de banco (ADR-0028); clients de tenant (ADR-0037); federação e grupos do Entra (ADR-0026/0036); concessões de elevação (ADR-0031) e de admin de tenant; catálogo de permissões (ADR-0039).

**Toda escrita feita com essa autoridade é auditada de forma síncrona e fail-closed.** O registro vai para o LogStream, no **tenant administrado**, pela identidade de auditoria da instalação (ADR-0031), com a pessoa como ator, a ação, o recurso, e em `Metadata` a aplicação (`client_id` do token, o `act` do pedido) e a autoridade usada. Falha ao gravar recusa a operação **antes** de ela acontecer; sem identidade de auditoria configurada (`SecureGate:Audit`), o escopo nunca é emitido e as rotas recusam a autoridade. Escrita feita com `securegate:admin` segue como hoje.

### Consequências

- A Intranet pode trocar o client de máquina pelo token da pessoa: um comprometimento dela passa a alcançar só os tenants que a pessoa logada administra, e a trilha da plataforma guarda a pessoa e a aplicação.
- **O client de máquina com `securegate:admin` não muda**: segue sendo credencial de instalação. Migrar a área administrativa para o token da pessoa é decisão do adotante, recomendada por esta ADR.
- **Riscos residuais aceitos:** incluir membro por e-mail revela ao admin de tenant se um e-mail existe na instalação (`404` × `409`); o admin de tenant pode dar perfis do tenant dele a qualquer pessoa da instalação — o efeito fica contido no tenant administrado.
- **Escrita administrativa por admin de tenant depende do LogStream no ar.** É a mesma consequência assumida para a elevação.
- **Emenda à ADR-0031:** a identidade de auditoria passa a escrever também no tenant administrado, além do tenant de quem elevou.
- A bateria negativa é provada por mutação: concessão em outro tenant não passa; concessão revogada não passa na requisição seguinte; operação de pessoa ou revogação de sessão sobre conta administrativa responde `403`; tenant de plataforma recusa concessão; rota não marcada recusa o escopo; client de tenant não recebe o escopo; sem concessão o escopo não sai no login nem na renovação; sem segundo fator não há code; operação de pessoa por tenant não dono responde `404`; trilha indisponível recusa a escrita.
- `Secco.SecureGate.Client` ganha as rotas de concessão e de "meus tenants": minor.
- **Fora desta ADR:** criar tenant e nomear admin pelo admin de tenant (o operador cria e concede no v1 — volta com evidência de uso); concessão com prazo; acesso do admin de tenant a banco, clients e federação.

---

## ADR-0045: Diretório de pessoas do tenant para client de produto — escopo `directory:read`

**Status:** Aceita
**Data:** 2026-10-19
**Complementa:** ADR-0037 (client de produto vinculado a tenant) e ADR-0040 (pessoa membro de vários tenants)

### Contexto

A issue [#40](https://github.com/rafsecco/secco-platform/issues/40) (`adopter-demand`) aponta que um produto não tem como saber quem são as pessoas do próprio tenant sem `securegate:admin`. O `secco-intranet` lê nome, e-mail e status das pessoas para o Diretório, para o organograma e para escolher destinatários de notificação, inclusive em segundo plano, sem pessoa logada. Faz isso com um client de máquina que administra **todos** os tenants da instalação: o maior privilégio da plataforma para a operação de menor risco. A ADR-0044 não resolve, porque um funcionário comum não tem concessão de admin de tenant e o envio de notificação não tem pessoa nenhuma.

Alternativas avaliadas:

- **Aceitar o novo escopo em `GET /api/v1/tenants/{tenantId}/users`**, conferindo o tenant da rota contra o do token. A resposta administrativa traz dados de segurança (segundo fator, logins externos, estado de bloqueio) e amarraria o diretório à evolução da API administrativa. Descartada.
- **Escopo também para client interativo** (o token do funcionário lista os colegas direto do front). Mais exposição sem caso real: o diretório que a pessoa vê passa pelo backend do produto. Descartada no v1.
- **Cópia local das pessoas no produto**, alimentada por um admin. Desatualiza e duplica o cadastro de que a plataforma é dona. Descartada (recusada pelo adotante).

### Decisão

**Escopo `directory:read`, concedível só a client de produto de máquina** (`client_credentials`, vinculado a tenant, ADR-0037). Client interativo e client de plataforma não o recebem. O token já leva o `tenant_id` do client, contido pelo `TenantResolver`.

**Rota própria, com o tenant vindo do token:** `GET /api/v1/directory/people` (paginada, `PageRequest`/`PagedResult`, máximo de 100 por página) e `GET /api/v1/directory/people/{userId}`. Não há tenant na URL, então o chamador não consegue pedir outro tenant; pessoa fora do tenant responde `404`, igual a inexistente.

**Campos mínimos de diretório:** `id`, `email`, `displayName`, `isActive` e `isOwner` (dono ou membro, ADR-0040). Nada de segundo fator, logins externos, sessões, perfis ou motivo de bloqueio.

**Quem aparece:** as pessoas donas do tenant e as membras com vínculo **ativo**. Vínculo suspenso não aparece. Conta desativada aparece com `isActive = false`, para o produto decidir (por exemplo, não notificar).

### Consequências

- O `secco-intranet` troca o client de máquina com `securegate:admin` por um client de produto do próprio tenant com `directory:read` no Diretório e nas notificações.
- **Risco aceito:** um client com o escopo enumera as pessoas do próprio tenant — é a função pedida. O isolamento entre tenants vem do `tenant_id` do token, e o filtro e o RLS da ADR-0041 valem por baixo.
- `Secco.SecureGate.Client` ganha as rotas de diretório (minor).

---

## ADR-0046: Custódia do token da pessoa para relying party — pacote `Secco.SDK.RelyingParty`

**Status:** Aceita
**Data:** 2026-10-19
**Complementa:** ADR-0023 (relying party on-behalf-of), ADR-0032 (sessões e revogação) e ADR-0035 (estado compartilhado)

### Contexto

A issue [#38](https://github.com/rafsecco/secco-platform/issues/38) (`adopter-demand`) aponta que, para chamar a plataforma on-behalf-of a pessoa logada (ADR-0044), uma aplicação precisa guardar e renovar o token dela, e só o AdminPortal sabe fazer isso, com código próprio: cofre no servidor com o cookie levando só o id da sessão, renovação perto do vencimento com rotação do refresh token, sessão perdida devolvendo ao login, e uma fábrica do client com o Bearer da pessoa. São cerca de 400 linhas de código de segurança. A Intranet teria de copiá-las, e duas cópias divergem.

Alternativas avaliadas:

- **Copiar o código do AdminPortal para cada aplicação.** Correção feita numa cópia não chega na outra. Descartada.
- **Access token direto no cookie (`SaveTokens = true`).** Cookie grande, e o token fica com o navegador até expirar, mesmo depois da revogação da ADR-0032. Descartada.
- **Dentro do `Secco.SDK.AspNetCore`.** Ele arrasta Hangfire e o resto do runtime de produto; foi por isso que o `Secco.SDK.ClientCredentials` nasceu separado. Descartada.

### Decisão

**Pacote novo `Secco.SDK.RelyingParty`**, para aplicação com cookie + OIDC code/PKCE (ADR-0023):

- **Registro junto do `AddOpenIdConnect`:** no `OnTokenValidated`, access e refresh token vão para o **cofre no servidor**; o cookie leva só o id da sessão. Cookie com sessão que não está no cofre é rejeitado e leva a novo login.
- **Cofre sobre `IDistributedCache`** (ADR-0035: estado que todas as instâncias precisam ver). Provider em memória hoje; Redis quando houver réplicas. A porta é pública para quem quiser outro armazenamento.
- **Provedor do token da pessoa, com renovação sob demanda:** quem pede o token recebe um válido; perto do vencimento ele é renovado com o refresh token, e o refresh rotacionado substitui o anterior no cofre. Renovação recusada (sessão revogada, ADR-0032) ou sessão ausente apagam a entrada e levam a novo login.
- **`DelegatingHandler` que anexa o Bearer da pessoa a qualquer `HttpClient`**: serve para o `Secco.SecureGate.Client` e para os clients de outros produtos, sem a SDK depender de nenhum client.
- **Escopos concedidos expostos**: a aplicação lê os escopos do token atual (por exemplo, se a pessoa recebeu `securegate:tenant-admin`, que a ADR-0044 filtra em silêncio) para decidir o que mostrar.
- **O AdminPortal passa a usar o pacote** e apaga a implementação própria.

### Consequências

- Uma relying party nova adota a custódia com algumas linhas de composição; correção de segurança chega a todas por versão de pacote.
- **Risco assumido:** o cofre guarda refresh tokens. Com o provider em memória, reiniciar o processo leva todos a novo login — perda tolerável. Com Redis, o Redis passa a guardar credencial e entra no backup e no controle de acesso com esse peso.
- A sessão segue a ADR-0032: o cofre não estende sessão revogada, porque a renovação é recusada pelo SecureGate.

---

## ADR-0047: Admin de tenant cria tenant e cuida do ciclo de vida dele

**Status:** Aceita — congelada pela ADR-0051
**Data:** 2026-10-19
**Complementa:** ADR-0044 (admin de tenant), ADR-0028 (provisionamento de banco) e ADR-0040

### Contexto

A issue [#39](https://github.com/rafsecco/secco-platform/issues/39) (`adopter-demand`) traz a evidência de uso que a ADR-0044 pediu para reabrir "criar tenant e nomear admin pelo admin de tenant". No `secco-intranet`, quem cria os tenants dos outros sistemas da empresa é o `intranet-admin`. Com a ADR-0044 ele administra pessoas, vínculos e perfis, mas criar, ativar e desativar tenant e provisionar banco seguem só com o operador, e a Intranet mantém o client de máquina com `securegate:admin` só por isso.

A análise achou dois caminhos de escalada nas operações pedidas:

1. **Federação.** Quem configura o `tid` da federação de B pode apontá-lo para um diretório Entra que controla, criar ali um usuário com o e-mail de uma pessoa dona em B e entrar como ela (a ADR-0026 casa por e-mail dentro do `tid` registrado). É tomada de conta, inclusive de outra conta administrativa.
2. **Desativação.** Pela ADR-0040, tenant dono desativado bloqueia a pessoa em todos os sistemas. O admin de B que desativa B tranca qualquer admin de C que seja dono em B.

Alternativas avaliadas:

- **Criar tenant como parte da concessão de admin de tenant.** Toda pessoa com concessão poderia multiplicar tenants. Descartada: criar é concessão própria.
- **A aplicação concede a admin logo depois de criar, com o client de máquina.** É a aplicação fabricando autoridade com credencial de instalação. Descartada (recusada pelo adotante).
- **Federação pelo admin de tenant.** Tomada de conta descrita acima. Fica com o operador.
- **Connection string manual pelo admin de tenant.** Apontar o banco de B para o banco de outro tenant quebra o isolamento da ADR-0005. Fica com o operador.

### Decisão

**Concessão separada "pode criar tenant"** (`tb_tenant_creator_grants`, uma por pessoa), gerida só pelo operador (`PUT`/`DELETE /api/v1/tenant-creators/{userId}`, `GET /api/v1/tenant-creators`). A autoridade usada é o mesmo escopo `securegate:tenant-admin` da ADR-0044: o escopo sai no login para quem tem concessão de admin **ou** de criação, com as mesmas exigências (segundo fator, auditoria configurada).

**Criar tenant:** `POST /api/v1/tenants` aceita a autoridade de admin de tenant quando a pessoa tem a concessão de criação. Na **mesma transação**, o tenant nasce e a pessoa recebe a concessão de admin sobre ele (ADR-0044), registrada como concedida por ela mesma na criação. Contenção da autoexpansão:

- **limite configurável** de tenants criados por pessoa (`SecureGate:TenantAdmin:MaxCreatedTenantsPerPerson`, padrão 20), contado pelas concessões de admin nascidas de criação; acima dele, `409`;
- quem cria **não nomeia outros admins** (segue da ADR-0044);
- revogar a concessão de criação não revoga a administração dos tenants já criados — isso é ato separado do operador.

**Nos tenants que administra**, a autoridade passa a cobrir:

- **desativar**, com uma trava: desativar um tenant que seja **dono de alguma conta administrativa** (com concessão de admin ou de criação) responde `409`. **Reativar fica com o operador** — senão o admin de tenant desfaria uma suspensão feita por ele;
- **provisionar o banco no modo automático** (ADR-0028, só quando a automação está ligada) e **consultar o status** dos bancos. O provisionamento por esta autoridade é **restrito**: sem alvo, servidor, database ou login vindos do pedido — os nomes são os derivados do tenant —, só para os produtos da plataforma (lista fechada no código) e com **criação estrita**: se o database ou o login já existirem, nada é criado nem assumido. Sem isso, o admin de B apontaria o provisionamento para o database de outro tenant — diretamente, ou escolhendo um produto cujo nome derivado colidisse com o de outro tenant, já que produto e slug se juntam com `_` — e ganharia um login `db_owner` dentro dele.

**Ficam com o operador:** o provisionamento em modo script (devolve a senha gerada), o cadastro manual de connection string, a federação e os grupos do Entra, e os clients de tenant.

**Trilha:** toda escrita com essa autoridade segue a ADR-0044 — síncrona e fail-closed. **Emenda à ADR-0044 sobre o destino:** a entrada vai para o tenant administrado quando ele tem banco do LogStream no catálogo; senão, para o tenant dono de quem agiu, com o tenant administrado na `Metadata`. Sem a emenda, um tenant recém-criado — que ainda não tem banco no LogStream — recusaria a primeira escrita dele, que é justamente provisionar o banco, e qualquer tenant sem LogStream ficaria fora do alcance do admin de tenant. A criação é gravada no tenant dono de quem criou.

### Consequências

- O `secco-intranet` pode aposentar o client de máquina com `securegate:admin` nas operações de tenant, depois de adotar a custódia do token da pessoa (ADR-0046).
- **Risco aceito:** uma pessoa com concessão de criação cria até o limite de tenants vazios. Eles não alcançam dados de ninguém e ficam visíveis ao operador.
- A trava de desativação pode impedir o admin de desativar o próprio tenant enquanto ele for dono de contas administrativas; o operador desativa. Um tenant desativado pelo admin de tenant só volta pelo operador.
- A bateria negativa é provada por mutação: sem concessão de criação não cria; limite respeitado; a concessão automática só cobre o tenant criado; desativação com conta administrativa dona recusa; reativação recusa a autoridade; provisionamento em modo script, com nomes, alvo ou produto vindos do pedido, ou sobre database ou login já existentes, e connection string manual recusam a autoridade; federação recusa; trilha indisponível recusa a escrita.
- `Secco.SecureGate.Client` ganha as rotas de concessão de criação (minor).

---

## ADR-0048: Admin de tenant lê o catálogo de permissões e gere clients de máquina do tenant

**Status:** Aceita — congelada pela ADR-0051
**Data:** 2026-10-21
**Complementa:** ADR-0044 (admin de tenant), ADR-0037 (clients de produto) e ADR-0039 (catálogo de permissões)

### Contexto

As issues [#41](https://github.com/rafsecco/secco-platform/issues/41) e [#42](https://github.com/rafsecco/secco-platform/issues/42) (`adopter-demand`) vêm do mesmo passo do `secco-intranet`: aposentar o client de máquina com `securegate:admin` depois de adotar as ADR-0044, 0046 e 0047. Restavam duas operações que a ADR-0044 deixou com o operador:

1. **Ler o catálogo de permissões** (ADR-0039). O admin de tenant já grava as permissões dos perfis (`SetRolePermissions`), mas não lê o que existe para gravar; a tela de perfil monta as caixas por produto a partir do catálogo.
2. **Gerir os clients do tenant** (ADR-0037): os de máquina, para o sistema gravar log e notificar, e os de login, para o SSO dos funcionários no sistema (ADR-0040).

A análise do client de login achou um caminho de impersonação. Quem controla a redirect URI de um client interativo do tenant B recebe o code de **qualquer pessoa com vínculo em B** — inclusive membros que pertencem a outros tenants, outros admins e operadores que sejam membros — e, pelo SSO com consentimento implícito, sem tela nenhuma. Com o code e o secret do client, o admin de B obtém token e refresh **em nome da pessoa** nos produtos de B. É o "acessar como" da issue #6, sem nenhuma das regras dela e sem a pessoa saber. O PKCE não protege, porque quem monta o fluxo é o próprio atacante. Alterar a redirect URI de um client interativo existente tem o mesmo efeito.

Alternativas avaliadas:

- **Novo escopo de leitura do catálogo.** Mais um escopo para o client de plataforma pedir, sem ganho: quem lê é a mesma pessoa que já grava. Descartada.
- **Catálogo filtrado pelos produtos com banco no tenant administrado.** Não protege nada — o catálogo não é segredo — e quebra a tela de um tenant ainda sem banco. Descartada.
- **Clients de login com redirect URI restrita a hosts aprovados pelo operador.** Contém o desvio do code a aplicações da empresa, mas é configuração e superfície novas sem pedido real. Fica para quando houver evidência.

### Decisão

**Leitura do catálogo de permissões pela autoridade de admin de tenant.** `ListPermissionCatalog` e `GetPermissionCatalog` passam a aceitar `securegate:tenant-admin`, o catálogo inteiro, para quem tem **alguma** concessão (de admin de qualquer tenant ou de criação, ADR-0047) com a conta em dia. São rotas sem tenant: a conferência é a da pessoa, não a de um tenant. Leitura não vai à trilha. Publicar o catálogo continua com cada produto (`permissions:<produto>`).

**Clients de máquina do tenant pela autoridade de admin de tenant.** Nos tenants que administra, a pessoa lista e detalha todos os clients, e cria, altera, rotaciona o secret e revoga os de tipo `client_credentials`. O efeito fica contido no tenant: o token do client sai com o `tenant_id` dele, só escopos de produto (lista fechada, ADR-0037) e perfis do próprio tenant. Toda escrita é auditada como na ADR-0044; o secret nunca vai à trilha.

**Clients de login continuam com o operador.** Pela autoridade de admin de tenant, criar um client `authorization_code` e alterar, rotacionar ou revogar um client interativo existente respondem `403`. Listar e detalhar continuam abertos.

### Consequências

- O `secco-intranet` pode aposentar o client de máquina com `securegate:admin` na tela de perfis e nas credenciais de máquina dos sistemas que o `intranet-admin` administra. As credenciais de login (SSO) de um sistema novo seguem pedidas ao operador.
- **Risco aceito:** um client de máquina criado pelo admin de tenant **continua valendo depois que a concessão dele é revogada** — como os perfis que ele deu. O operador vê os clients do tenant e os revoga.
- **Risco aceito:** o admin de tenant lê o catálogo de toda a instalação, e com ele quais produtos existem. Nomes e descrições de permissões não são segredo (ADR-0021).
- A bateria negativa é provada por mutação: catálogo sem concessão recusa; client interativo criado, alterado, rotacionado ou revogado pela autoridade recusa; client de outro tenant recusa.
- `Secco.SecureGate.Client` não muda de forma: as rotas já existem.

---

## ADR-0049: Operador da instalação por designação — conta de outro tenant, por troca curta com reautenticação

**Status:** Aceita — congelada pela ADR-0051
**Data:** 2026-10-21
**Complementa:** ADR-0030 (instalação soberana), ADR-0040 (operador decidido pelo tenant do login), ADR-0031 (troca de token) e ADR-0044/0048 (admin de tenant)

### Contexto

A issue [#43](https://github.com/rafsecco/secco-platform/issues/43) (`adopter-demand`) traz o último uso do client de máquina com `securegate:admin` no `secco-intranet`. Quando a instalação escolhe a Intranet como centralizador da configuração dos serviços da plataforma, quem administra a instalação **é** o `intranet-admin`. As ADR-0044 a 0048 deram a ele quase tudo, mas algumas operações ficaram com o operador por motivos que valem contra um admin de **tenant**: federação Entra, reativação de tenant, provisionamento em modo script, connection string manual e clients de login. E nenhuma pessoa da Intranet pode ser operadora: pela ADR-0040, `securegate:admin` só sai quando o tenant do login é o de plataforma **e** a conta é dele; uma conta do tenant de plataforma, por sua vez, não entra na Intranet, cuja tenancy recusa um tenant sem banco dela.

O pedido difere do workaround recusado pela ADR-0044 ("dar `installation-operator` ao `intranet-admin`"): lá seria a aplicação se promovendo; aqui é **a instalação que decide**, por configuração, quem a opera — a soberania da ADR-0030.

A análise achou que a plataforma não tinha como exigir "autenticou-se agora, com segundo fator": os tokens não carregavam `auth_time` nem `amr`, o `/connect/authorize` ignorava `max_age` e `prompt=login`, e o `IssuedUtc` do cookie de login se renova sozinho pela expiração deslizante.

Alternativas avaliadas:

- **Escopo de operador no token comum da pessoa, com `tenant_id`** (opção "b" da issue). A sessão comum — horas, com refresh — passaria a carregar poder de instalação, exatamente o que o risco de sessão sequestrada teme; e o token de operador com tenant reabriria a regra "claim × header" do LogStream (ADR-0024). Descartada.
- **Designação por API, concedida por um operador.** Um operador designado designaria outros, e a autoridade se perpetuaria fora do controle de quem configura a instalação. Descartada: a configuração é a única fonte.
- **Designação por e-mail.** O dono troca o próprio e-mail (ADR-0033); a designação não pode seguir um endereço. Descartada: é por id da conta.
- **Só token curto, sem reautenticação.** Um cookie roubado geraria token de operador. Descartada.
- **Segundo login na Intranet com conta de operador** (plano B do adotante). Duas contas por pessoa, e o `prompt=login` trocaria a conta da sessão do SecureGate no navegador, fazendo o SSO dos outros sistemas entrar como operador. Fica como plano do adotante, não da plataforma.

### Decisão

**Designação declarativa.** `SecureGate:InstallationOperators:Accounts` lista os ids das contas que a instalação designa operadoras. Nenhuma API designa ninguém; tirar a conta da lista revoga na requisição seguinte, porque a designação é relida a cada consulta. Sem designação, nada muda.

**Poder de operador como ato: troca curta.** O login da pessoa segue exatamente como hoje (tenant da Intranet, perfis da Intranet). Para operar, a aplicação troca o token da pessoa (RFC 8693, mesmo endpoint da elevação da ADR-0031, pedindo o escopo `securegate:admin`) por um **token de operador designado**:

- `sub` real, **sem `tenant_id`** (operador tenant-less, ADR-0024), **só** o escopo `securegate:admin`, nenhum papel;
- TTL de `SecureGate:InstallationOperators:TokenLifetimeMinutes` (padrão 10), com **teto de 15 minutos** — a configuração aperta, nunca afrouxa;
- **sem refresh**, e não troca de novo (nem elevação vira operador, nem operador renova o próprio TTL);
- marcado (`token_exchange = designated-operator`) e com `sver` — revogar a sessão da pessoa o derruba (ADR-0032).

A troca exige, todas: conta designada, conta em dia (bloqueio, desativação, tenant dono ativo), **versão de sessão do token de entrada igual à da conta** (`sver`, ADR-0032 — um token tirado antes de uma troca de senha ou de segundo fator não serve), **segundo fator cadastrado**, e a sessão de onde veio o token **autenticada com segundo fator** (`amr` contém `mfa`) **há no máximo** `RecentAuthenticationMinutes` (padrão 10, teto 15). Toda recusa é a mesma resposta da elevação, sem dizer o que faltou.

**Reautenticação recente na plataforma.** O SecureGate passa a:

- gravar no cookie de login o instante da autenticação — só as telas de login (senha, segundo fator, federado) o gravam; renovações do cookie o preservam;
- emitir `auth_time` e `amr` no access token e no id_token; a renovação preserva os da sessão (renovar não é autenticar de novo);
- respeitar `prompt=login` e `max_age` no `/connect/authorize`: a sessão do SecureGate sai, a pessoa autentica de novo, e a requisição volta sem o pedido de reautenticação (`prompt=none` junto responde `login_required`).

A aplicação pede a reautenticação antes da área de operação; um cookie roubado, sozinho, gera token comum, nunca token de operador.

**Cada requisição com o token de operador designado é conferida e cada escrita é auditada.** Um middleware — e não filtro de rota, porque o token abre todas as rotas de operador e nenhuma pode escapar — reconfere designação e conta a cada requisição e grava cada escrita na trilha **antes** de ela acontecer, síncrona e fail-closed (`503` e nada acontece), com a pessoa como ator, a aplicação como `act` e a ação prefixada `operador-designado.`. Destino: o tenant da rota quando ele tem banco do LogStream no catálogo; senão, o tenant dono da pessoa (mesma regra da ADR-0047). A troca também vai à trilha, no tenant dono, antes da emissão. Sem identidade de auditoria configurada, a troca é recusada.

**A conta designada é conta administrativa** (ADR-0044): quem administra o tenant dono dela não reseta senha ou segundo fator, não desativa, não mexe em sessões nem em logins externos; e esse tenant não pode ser desativado pela autoridade de admin de tenant.

### Consequências

- O `secco-intranet` pode aposentar o client de máquina com `securegate:admin` por completo: as operações de operador passam a ser feitas on-behalf-of a pessoa designada, com reautenticação antes, token curto e trilha de cada escrita.
- **Risco residual aceito:** quem tiver senha, segundo fator **e** sessão da pessoa designada opera a instalação — é a definição de operador. A defesa é o poder ser um ato curto, reautenticado e auditado, não um estado permanente na aplicação.
- **Designação com login federado:** o `amr` do Entra não atravessa a federação (ADR-0026), então a conta designada precisa autenticar com o segundo fator do SecureGate para trocar.
- **Revogar exige mudar a configuração.** Com provedor de configuração recarregável, vale na requisição seguinte; sem ele, no próximo reinício — e o token já emitido morre em até 15 minutos de qualquer forma.
- `auth_time` e `amr` passam a existir em todo token de pessoa — informação padrão do OIDC, útil a qualquer aplicação que queira exigir reautenticação.
- A bateria negativa é provada por mutação: sem designação, versão de sessão antiga, sem segundo fator cadastrado, sessão sem `amr=mfa`, autenticação antiga, designação retirada, escrita sem trilha, trilha recusada, conta designada exposta ao admin de tenant, `prompt=login`/`max_age` ignorados, renovação perdendo o `auth_time`, token de operador com tenant. A recusa de re-troca é defesa em profundidade: o token de operador não carrega `amr`, então a exigência de segundo fator já o recusa antes.
- Sem contrato HTTP novo: a troca usa o endpoint de token do OIDC.

---

## ADR-0050: Relying party exige autenticação recente e troca o token da pessoa — extensão do `Secco.SDK.RelyingParty`

**Status:** Aceita — congelada pela ADR-0051
**Data:** 2026-10-21
**Complementa:** ADR-0046 (custódia do token da pessoa), ADR-0049 (operador designado) e ADR-0031 (elevação)

### Contexto

A issue [#44](https://github.com/rafsecco/secco-platform/issues/44) (`adopter-demand`) aponta o passo que duas ADRs já pedem a uma relying party e que o `Secco.SDK.RelyingParty` não cobre: **exigir autenticação recente** (a ADR-0049 condiciona a troca de operador a uma autenticação com segundo fator dos últimos minutos, e espera que a aplicação peça `prompt=login`/`max_age` antes da área de operação) e **trocar o token da pessoa por um token curto** (RFC 8693, ADR-0031 e ADR-0049). Sem o pacote, cada aplicação implementaria o desafio de reautenticação, a chamada de troca, a guarda do token curto no servidor e o tratamento da recusa genérica — código de segurança que a ADR-0046 existe para não ser copiado. A Intranet precisaria disso duas vezes (modo centralizador e leitor de logs).

Alternativas avaliadas:

- **Uma API por capacidade** (`GetOperatorTokenAsync`, `GetElevatedTokenAsync`). Duplicaria o mesmo mecanismo; o SecureGate já decide a troca pelo escopo pedido. Descartada: uma API por escopo.
- **Token trocado no cookie.** O mesmo motivo da ADR-0046: o token ficaria com o navegador até vencer. Descartada.
- **Política de autenticação recente lendo a claim do cookie.** O cookie da aplicação reflete o id_token do login; a fonte de verdade é o access token custodiado, que a renovação mantém coerente. Descartada.
- **Troca automática em segundo plano** (renovar o token curto sozinho perto do vencimento). Poder de operador não pode nascer sem uma ação da pessoa. Descartada.

### Decisão

O `Secco.SDK.RelyingParty` ganha três peças, sem mudança no SecureGate:

**Exigir autenticação recente.** Um requisito de autorização (`RequireSeccoRecentAuthentication(maxAge, requireMfa: true)`) confere o `auth_time` e o `amr` do access token da sessão no cofre. Quando a autenticação é mais antiga que `maxAge` (ou não se sabe quando foi), o pacote desafia o esquema OIDC com `prompt=login` e `max_age`, voltando à mesma URL. Quando a autenticação é recente mas não tem `mfa`, a resposta é `403` — desafiar de novo levaria uma conta sem segundo fator a um laço de login. Vale para MVC, Razor Pages e APIs mínimas (middleware de autorização).

**Token trocado por escopo.** `ISeccoExchangedTokenProvider.GetAsync(scope)` faz a troca RFC 8693 no endpoint de token, com o access token atual da pessoa como `subject_token`, e guarda o token curto **no cofre da sessão**, nunca no cookie, até perto do vencimento. A renovação do token da pessoa preserva os tokens trocados; encerrar a sessão os descarta. O resultado diz se o token foi emitido ou recusado; como a recusa da plataforma é genérica de propósito, quem decide pedir reautenticação é a aplicação. A mesma API serve à elevação (`logstream`) e ao operador designado (`securegate:admin`).

**Anexar o token trocado.** `IHttpClientBuilder.AddSeccoExchangedToken(scope)` anexa o token trocado; sem token, lança uma exceção específica de reautenticação, que a aplicação trata redirecionando — nunca segue sem autorização nem com o token comum da pessoa.

### Consequências

- A Intranet e qualquer produto com elevação ou operação designada usam a troca sem escrever código de segurança próprio; correções chegam por versão de pacote.
- **Risco assumido:** o cofre passa a guardar também tokens curtos de alto privilégio. O peso é o mesmo da ADR-0046 (o cofre já guarda refresh tokens) e o TTL deles é de minutos.
- A troca só acontece quando a aplicação pede — e a política de autenticação recente é o que faz a pessoa estar de fato presente quando ela acontece.
- `Secco.SDK.RelyingParty` 0.2.0, só acréscimos.

---

## ADR-0051: Administração da instalação só pelo AdminPortal — produto-portal administra só o próprio tenant

**Status:** Aceita
**Data:** 2026-10-21
**Substitui parcialmente:** ADR-0030 (o papel do AdminPortal e da Intranet como console; o resto da ADR-0030 segue valendo)
**Congela:** ADR-0044, ADR-0047, ADR-0048, ADR-0049 e ADR-0050 (seguem Aceitas e entregues, sem consumidor planejado)

### Contexto

A ADR-0030 fez da Intranet o "portal completo" de quem a usa, incluindo a operação cross-tenant, e reduziu o AdminPortal a console mínimo para quem adota sem a Intranet. Para que a Intranet administrasse os outros tenants da empresa sem um client de máquina com `securegate:admin`, a plataforma construiu em sequência a autoridade de admin de tenant on-behalf-of (ADR-0044), a criação de tenant por essa autoridade (ADR-0047), a leitura do catálogo e a gestão de clients de máquina (ADR-0048), o operador por designação (ADR-0049) e a troca de token com reautenticação no SDK (ADR-0050). Cada uma exigiu concessão explícita, trilha síncrona fail-closed, lista versionada de rotas e bateria de mutação. As issues #45 (segundo fator por nível) e #46 (operador designado por perfil) continuavam a mesma trilha, e do lado da Intranet ainda faltavam o leitor de logs por elevação, a área on-behalf-of e o modo centralizador.

O dono do produto avaliou que o custo não se paga: a cada passo, a Intranet precisava de uma capacidade nova da plataforma, e a superfície de segurança crescia para servir a uma tela que o AdminPortal já tinha em boa parte.

Alternativas avaliadas:

- **Seguir com a centralização na Intranet** (ADR-0030 como está). Descartada pelo custo descrito acima.
- **Um SecureGate por sistema**, cada produto isolado na própria instância. Perde o SSO entre os sistemas da empresa e o desligamento de pessoa num lugar só: quem sai precisaria ser desativado em N emissores, e a federação com o Entra só barra login novo — as sessões já abertas em cada instância seguem vivas até o refresh expirar, porque a guarda de conta (`AccountStateGuard`) confere o estado local. Descartada para identidade.
- **Remover o que foi entregue nas ADR-0044 a 0050.** Está testado e fica inerte sem concessão ou configuração; arrancar custa mais que congelar. Descartada por ora, com condição de revisão (abaixo).

### Decisão

- **O `Secco.AdminPortal` é o console de operação da instalação**, com ou sem Intranet. Criar e desativar tenant, provisionar banco, federação, clients e vínculos entre tenants são do operador da instalação, por ele.
- **Produto-portal (a Intranet ou outro) administra só o próprio tenant**: perfis, pessoas e configuração dele. Não cria nem administra outros tenants.
- **O SecureGate continua compartilhado por instalação** — é ele que dá SSO entre os sistemas da empresa e desligamento de pessoa num lugar só (ADR-0040).
- **LogStream e NotificationHub podem rodar uma instância por sistema** quando conveniente. "Duplicar" significa subir outra instância da mesma imagem ou pacote — **nunca copiar código de produto** para dentro de outro sistema, o que multiplicaria cada correção de segurança.
- **As ADR-0044, 0047, 0048, 0049 e 0050 ficam congeladas**: continuam valendo para quem as configurar, mas não recebem novas capacidades. As issues #45 e #46 são encerradas sem implementação.

### Consequências

- **Risco assumido (ADR-0020):** a superfície das ADRs congeladas continua no código e nas rotas. Está inerte — sem concessão (`tb_tenant_admin_grants`, `tb_tenant_creator_grants`) ou sem `SecureGate:InstallationOperators:Accounts`, nada é emitido —, mas é código que precisa continuar correto e coberto pelas baterias de mutação existentes. **Condição de revisão:** se nenhum adotante configurar essas capacidades até a próxima revisão geral, abrir ADR de remoção.
- **O AdminPortal herda trabalho:** hoje ele lista tenants e gere usuários, perfis e banco por connection string manual, mas não cria tenant, não ativa/desativa, não provisiona banco (ADR-0028), não configura federação (ADR-0026), não gere clients (ADR-0037) nem vínculos (ADR-0040). Essas telas entram no backlog; a API para todas já existe sob `securegate:admin`.
- **A duplicação de telas aceita pela ADR-0030 diminui**: a Intranet deixa de replicar a operação cross-tenant.
- A elevação explícita para ler log cross-tenant (ADR-0031) e a hierarquia da ADR-0030 (operador de instalação × admin de tenant) seguem intactas.

---

## ADR-0052: Gestão do próprio tenant por client de máquina — escopo `securegate:tenant-manage`

**Status:** Aceita
**Data:** 2026-10-21
**Complementa:** ADR-0037 (client de produto vinculado a tenant) e ADR-0045 (escopo só de client de produto). Reusa o mecanismo de rotas abertas uma a uma e a trilha da ADR-0044, congelada pela ADR-0051, sem reabri-la.

### Contexto

Com a ADR-0051, a Intranet administra só o próprio tenant: cria perfis de setor, lista pessoas, atribui e retira perfis, desativa contas e encerra sessões. Faz isso com um client de plataforma (sem tenant) que tem `securegate:admin`, escopo que alcança a instalação inteira. Quem roubar esse secret cria tenant, lê e altera pessoas de todos os tenants e gera script de banco com senha. É o achado mais previsível do pentest planejado.

Alternativas avaliadas:

- **On-behalf-of a pessoa** (ADR-0044). Pronta, e a trilha registraria a pessoa. Exige segundo fator de todo `intranet-admin`, que era exatamente o que a issue #45 queria tornar opcional e foi descartado pela ADR-0051. Escolhê-la reabriria a trilha congelada. Descartada.
- **Aceitar o novo escopo em todas as rotas que já aceitam admin de tenant.** Incluiria criação e desativação de tenant, provisionamento, clients e vínculos — operações que a ADR-0051 deixou com o operador. Descartada: lista própria, menor.
- **Manter `securegate:admin` e compensar com monitoramento.** Não reduz o alcance do secret. Descartada.

### Decisão

**Escopo `securegate:tenant-manage`, só de client de produto `client_credentials`** vinculado a tenant (ADR-0037), no molde do `directory:read` (ADR-0045). Client interativo e client de plataforma não o recebem (a configuração de `SecureGate:PlatformClients` com ele derruba o startup). Nunca no tenant de plataforma.

**Só o operador concede.** Criar, alterar, rotacionar ou excluir client com esse escopo pela autoridade de admin de tenant (ADR-0044/0048) responde `403`. Rotacionar está incluído porque entregaria o secret a quem não poderia ter criado o client.

**Rotas abertas uma a uma, por marca própria** (`.AllowTenantManage()`) com lista versionada imposta por teste de arquitetura (`tests/SecureGate/.../Architecture/tenant-manage-routes.txt`): `GetTenant`, `ListUsers`, `GetUser`, `AddUserRole`, `RemoveUserRole`, `SetUserDisplayName`, `DeactivateUser`, `ActivateUser`, `RevokeUserSessions`, `CreateRole`, `ListRoles`, `GetRole`, `SetRolePermissions`, `DeleteRole`. Fora, de propósito: criação/desativação de tenant, provisionamento, federação, clients, vínculos, convite, senha, segundo fator e login externo.

**Conferência por requisição:** tenant da rota igual ao `tenant_id` do token; tenant de plataforma recusado; nas rotas sobre uma pessoa que mudam o acesso dela (retirar perfil, nome, desativar/reativar, encerrar sessões), conta administrativa como alvo recusada — a mesma proteção da ADR-0044. A recusa é a resposta genérica da ADR-0044, que não diz se o tenant existe. O isolamento por baixo (ADR-0041, escopo de acesso e RLS) segue valendo.

**Trilha síncrona e fail-closed** no LogStream do próprio tenant, pelo mesmo caminho da ADR-0044, com o client como ator (`ActorType.Client`) e ação `gestao-do-tenant.<operationId>`. Sem auditoria configurada, escrita com o escopo recusa e leitura segue; gravação falhou, `503` e nada é escrito. Motivo: a trilha da própria Intranet não vê quem usa o secret roubado direto contra o SecureGate.

**Pacote:** `Secco.SecureGate.Client` ganha `AddSecureGateTenantManagementClient()`, que registra o client tipado pedindo o escopo novo com a credencial da subseção `Secco:SecureGate:TenantManagement`.

### Consequências

- A Intranet troca `securegate:admin` por um client de produto do próprio tenant. O secret dela passa a alcançar pessoas e perfis de um tenant, não a instalação.
- **Federação do tenant sai da Intranet** e fica só com o operador, pelo AdminPortal, que ganha a seção. Apontar o login de um tenant para outro diretório Entra é a operação de maior risco do conjunto.
- **Risco aceito:** depois de rotacionar ou revogar o client, ou de desativar o tenant, um token já emitido vale até expirar (5 minutos por padrão), igual a todo token de máquina (ADR-0032/0037). A emissão de token novo já recusa tenant desativado.
- **Risco aceito:** a trilha registra a máquina, não a pessoa que agiu na Intranet; quem foi a pessoa continua na auditoria da própria Intranet.
- Quebra no adotante: criar o client de produto e configurar a subseção antes de subir a versão que deixa de usar `securegate:admin`.
- **Bateria negativa provada por mutação: 10 de 10 detectadas** — âncora tenant da rota × token; tenant de plataforma (com client gravado direto no banco, que a API nunca cria); alvo administrativo; policy sem a marca; policy e segunda camada juntas; escrita sem trilha; máquina gravada como pessoa; admin de tenant rotacionando, e criando, client com o escopo; escopo aceito no tenant de plataforma. A segunda camada do filtro da `AllowTenantAdmin` só é detectável junto com a policy, por desenho.

---

## ADR-0053: Delegação na trilha de auditoria — chave `onBehalfOf` no `Metadata`, sem coluna nova

**Status:** Aceita
**Data:** 2026-10-22
**Complementa:** recurso `AuditEntry` do LogStream (issues #2 e #6) e ADR-0020

### Contexto

O "acessar como" (issue #6) exige que a trilha separe **quem agiu** de **em nome de quem**: o token representado leva o `sub` da pessoa e a claim `act` de quem representa (RFC 8693), e a trilha precisa contar a mesma história. O `AuditEntry` não tem campo para "em nome de quem". O backlog registrava a decisão sobre uma coluna `OnBehalfOfId` como urgente, "antes de a trilha ganhar volume", porque acrescentar coluna a tabela com anos de retenção seria caro.

Revisto, o argumento não se sustenta: acrescentar coluna anulável é barato no SQL Server e no PostgreSQL, sem reescrever a tabela. O custo real de adiar é outro — **história partida**: sem convenção, cada produto grava a delegação com uma chave própria no `Metadata`, e toda consulta "o que foi feito em nome de X" teria de conhecer todas elas para sempre. O "acessar como" segue sem demanda (ADR-0051).

Há ainda um risco que a coluna, sozinha, não resolve: um "em nome de" **declarado pelo chamador** é só a palavra dele. Na gestão do tenant por máquina (ADR-0052), a Intranet poderia informar a pessoa por trás da ação — e quem roubasse o secret informaria o nome que quisesse.

Alternativas avaliadas:

- **Coluna `OnBehalfOfId` agora**, sem consumidor: migration nos dois engines, contrato e client novos para um campo vazio. Descartada (YAGNI); entra junto com o recurso que a preencher.
- **Nada agora.** Cada produto inventaria sua chave. Descartada.

### Decisão

- **A delegação vai no `Metadata` da `AuditEntry`, sob a chave fixa `onBehalfOf`**, com o identificador (`sub`) da pessoa representada. `ActorId`/`ActorType` continuam sendo **quem de fato agiu**.
- **`onBehalfOf` só nasce de claim de token assinado** — o `sub` de um token cujo `act` identifica o ator. Nunca de campo do payload, header ou parâmetro informado pelo chamador. Sem token representado, a chave não aparece.
- **`act` no `Metadata` continua significando outra coisa:** a aplicação pela qual alguém agiu (ADR-0044). Não se confunde com `onBehalfOf`.
- Quando o "acessar como" for desenhado, a ADR dele decide se `onBehalfOf` vira coluna (filtrável, indexada). Os registros anteriores já estarão na chave fixa, então a migração é uma cópia, não uma arqueologia.

### Consequências

- Nenhum código muda hoje: nenhum produto grava delegação de pessoa por pessoa.
- Consulta por delegação, enquanto não houver coluna, é por conteúdo do `Metadata` — sem índice. Aceito: não há consumidor.
- A trilha da gestão por máquina (ADR-0052) continua registrando só o client; a pessoa por trás segue na auditoria do próprio produto, porque não há token da pessoa no caminho.

---

## ADR-0054: Trilha da gestão do próprio tenant guardada no SecureGate — o LogStream deixa de ser dependência

**Status:** Aceita
**Data:** 2026-10-22
**Emenda:** ADR-0052 (trilha síncrona e fail-closed "no LogStream do próprio tenant")

### Contexto

A issue [#48](https://github.com/rafsecco/secco-platform/issues/48) (`adopter-demand`) mostra que a ADR-0052 tornou o LogStream obrigatório para quem administra o próprio tenant. Toda escrita com `securegate:tenant-manage` grava antes no LogStream do tenant: sem `SecureGate:Audit` configurado responde `403`, e sem banco do LogStream para o tenant responde `503`. Para o dono do produto, LogStream e NotificationHub são opcionais, e a ADR-0051 os trata assim. Uma instalação só com SecureGate e Intranet não conseguia criar um setor.

O motivo da trilha continua válido: o produto não vê quem usa um secret roubado direto contra o SecureGate. A trilha não pode sair nem virar falha-aberta. O que muda é onde ela mora.

Alternativas avaliadas:

- **Trilha falha-aberta sem LogStream.** Perde a proteção contra o secret roubado. Descartada.
- **Local só quando o tenant não tem LogStream.** A trilha de uma mesma autoridade ficaria em dois lugares conforme o tenant, e quem investiga teria de saber onde procurar. Descartada.
- **Local mais cópia de melhor esforço no LogStream.** Duas fontes que podem divergir, e a cópia não daria garantia nenhuma. Descartada.
- **Gravar a trilha na mesma transação da operação.** É a garantia mais forte, mas as escritas passam por `UserManager`, pelo revogador de sessões (OpenIddict) e por mais de um `SaveChanges`. Seria preciso refazer todos os handlers. Descartada: "auditar antes" já garante que nada acontece sem registro.
- **Mover também as trilhas das ADR-0044/0047/0049.** Estão congeladas pela ADR-0051 e seguem como estão. Descartada.

### Decisão

**A trilha da gestão do próprio tenant por máquina passa a ser gravada no banco do SecureGate**, numa tabela de auditoria de autoridade, como fonte de verdade única, sem cópia no LogStream.

- **Formato do `AuditEntry` do LogStream:** tenant, tipo de ator (sempre `client` aqui) e id do ator, nome do ator, ação (`gestao-do-tenant.<operationId>`), tipo e id do recurso, `Metadata` em JSON (`authority`, `act` e o tenant administrado; a chave `onBehalfOf` da ADR-0053, quando vier) e o instante do **servidor**.
- **Síncrona e fail-closed, antes da operação:** o registro é gravado e confirmado antes de o handler rodar. Gravação falhou: `503` e nada é escrito. Uma tentativa que falha depois do registro fica na trilha, como já acontecia no LogStream.
- **Não depende de `SecureGate:Audit` nem do LogStream.** A autoridade `tenant-manage` funciona numa instalação só com o SecureGate.
- **Dado de tenant:** filtro `"Tenant"` do EF e Row-Level Security nos dois engines (ADR-0041), como as outras tabelas de tenant. Sem endpoint de alteração nem de exclusão.
- **Leitura só pelo operador:** `GET /api/v1/tenants/{tenantId}/audit-entries`, paginado, com `securegate:admin`, do mais recente para o mais antigo. O client com `tenant-manage` não lê a própria trilha: ela existe contra ele. O AdminPortal mostra a trilha na página do tenant.
- **Retenção opcional:** `SecureGate:Audit:RetentionDays`. Sem valor, a trilha não expira, como a auditoria do LogStream. Com valor, um serviço de fundo apaga, em lotes, o que passou da janela pelo instante do servidor. A limpeza é idempotente, então várias instâncias podem rodá-la.

### Consequências

- LogStream e NotificationHub voltam a ser opcionais para quem administra o próprio tenant. Para a Intranet, o SecureGate volta a ser a única dependência dura.
- **Risco assumido:** a credencial de runtime do SecureGate pode apagar a trilha direto no banco. Antes, quem tinha essa credencial não alcançava a trilha, que estava no LogStream. A separação perdida é real. Uma credencial de runtime sem `DELETE` nessa tabela (a limpeza da retenção rodaria com outra identidade) fica para uma ADR própria, se a ameaça justificar.
- Os admins do tenant não veem essa trilha no LogStream deles; só o operador a lê. A autoria da pessoa continua na auditoria do próprio produto (ADR-0052).
- As trilhas das ADR-0031, 0044, 0047 e 0049 continuam no LogStream.
- Contrato novo: `Secco.SecureGate.Client` ganha a leitura da trilha.
- **Bateria negativa provada por mutação: 10 de 10 detectadas.** Cobre: escrita sem trilha, gravação que não acontece, falha de gravação virando sucesso, gravação sem ator, máquina gravada como pessoa, leitura pela máquina, leitura pelo admin de tenant, corte da retenção invertido, retenção fora da faixa aceita e ordem da leitura.

---

## Backlog de ADRs futuras

- Política de retenção e conformidade LGPD por produto
- Estratégia de deploy (contêiner: onde? Azure/AWS/on-prem do cliente?)
- Roadmap público e política de suporte a versões
