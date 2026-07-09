# Secco Platform — Architecture Decision Records (ADR)

> Fonte da verdade arquitetural do ecossistema Secco Platform.
> Nenhum código deve contradizer uma ADR com status **Aceita**.
> Para mudar uma decisão, cria-se uma nova ADR que **substitui** a anterior — ADRs nunca são editadas retroativamente nem apagadas.

**Última atualização:** 2026-07-09 (ADR-0007 detalhada, ADR-0021 adicionada)
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

## Backlog de ADRs futuras

- Estratégia de cache distribuído (Redis) e invalidação
- Idempotência em endpoints de escrita
- Política de retenção e conformidade LGPD por produto
- Estratégia de deploy (contêiner: onde? Azure/AWS/on-prem do cliente?)
- Roadmap público e política de suporte a versões
