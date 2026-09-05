# Secco Platform — Architecture Decision Records (ADR)

> Fonte da verdade arquitetural do ecossistema Secco Platform.
> Nenhum código deve contradizer uma ADR com status **Aceita**.
> Para mudar uma decisão, cria-se uma nova ADR que **substitui** a anterior — ADRs nunca são editadas retroativamente nem apagadas.

**Última atualização:** 2026-07-19 (ADR-0026 proposta)
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

## Backlog de ADRs futuras

- Estratégia de cache distribuído (Redis) e invalidação
- Idempotência em endpoints de escrita
- Política de retenção e conformidade LGPD por produto
- Estratégia de deploy (contêiner: onde? Azure/AWS/on-prem do cliente?)
- Roadmap público e política de suporte a versões
