# Secco Platform — Architecture Decision Records (ADR)

> Fonte da verdade arquitetural do ecossistema Secco Platform.
> Nenhum código deve contradizer uma ADR com status **Aceita**.
> Para mudar uma decisão, cria-se uma nova ADR que **substitui** a anterior — ADRs nunca são editadas retroativamente nem apagadas.

**Última atualização:** 2026-07-04
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

**Status:** Proposta
**Data:** 2026-07-04

### Contexto
A plataforma tem múltiplos produtos (SecureGate, LogStream, etc.) que compartilham `Secco.SharedKernel` e `Secco.SDK`, ambos em evolução rápida. Time pequeno. Multi-repo exigiria publicar pacote e atualizar N repositórios a cada mudança no kernel, criando atrito e divergência de versões. Ao mesmo tempo, é requisito que uma empresa possa adotar apenas um produto isoladamente.

### Decisão
Um único repositório `secco-platform` contendo todos os produtos. A independência de adoção é garantida por **artefatos** (pacotes NuGet e deployables independentes), não por repositórios. Extração para repositório próprio só ocorrerá se um produto for aberto como open source com contribuidores externos, via nova ADR.

Estrutura raiz:

```
secco-platform/
├── docs/adr/
├── eng/                      # Directory.Build.props, analyzers, nuget.config
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
└── Secco.Platform.sln           # + solution filters (.slnf) por produto
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

**Status:** Proposta
**Data:** 2026-07-04

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
**Data:** 2026-07-04

### Contexto
Identidade deve ser um serviço da plataforma, não uma preocupação de cada produto.

### Decisão
- Secco.SecureGate é o único emissor de tokens (OIDC provider) da plataforma.
- Todas as APIs validam JWT via `AddSeccoAuthentication()` do SDK (authority = SecureGate, validação por JWKS).
- Autorização por policies nomeadas em constantes no SharedKernel; claims padronizadas: `sub`, `tenant_id`, `roles`, `scope`.
- Comunicação serviço-a-serviço usa client credentials flow.

### Consequências
- Nenhum produto armazena credenciais de usuários.
- SecureGate é dependência de runtime de todos — exige SLA e HA superiores aos demais.

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
| Tabelas/colunas | snake_case (PostgreSQL) | `log_entries.tenant_id` |
| Casos de uso | verbo + substantivo | `CreateLogEntryHandler` |
| Testes | `Metodo_Cenario_Resultado` | `Create_WhenTenantMissing_ReturnsFailure` |
| Branches | `feature/`, `fix/`, `chore/` | `feature/logstream-retention` |
| Commits | Conventional Commits | `feat(logstream): add retention policy` |

---

## ADR-0011: Pacotes NuGet, versionamento e feed

**Status:** Proposta
**Data:** 2026-07-04

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
- **Integration:** Infrastructure + Api com **Testcontainers** (PostgreSQL, Redis reais) e `WebApplicationFactory`.
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

**Status:** Proposta
**Data:** 2026-07-04

### Decisão
- GitHub Actions com **path filters**: mudança em `src/LogStream/**` builda apenas LogStream (+ dependentes de kernel/SDK quando estes mudam).
- Pipeline por produto: build → testes → geração/validação de OpenAPI → pack de client.
- Publicação de NuGet disparada por tag (ADR-0011); deploy de serviço disparado por tag `logstream/v*`.
- Artefatos de deploy: imagens de contêiner.

---

## ADR-0015: Background processing

**Status:** Proposta
**Data:** 2026-07-04

### Contexto
Produtos precisarão de trabalho assíncrono (retenção de logs no LogStream, envio no NotificationHub). Ainda não há decisão madura.

### Decisão (proposta a validar)
Iniciar com `BackgroundService` nativo + fila em Redis para casos simples. Adotar um scheduler/queue dedicado (Hangfire ou similar) apenas quando um produto demonstrar necessidade real, via atualização desta ADR.

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

## Backlog de ADRs futuras

- Suporte a múltiplos providers de banco (PostgreSQL primeiro; abstração para SQL Server?)
- Estratégia de cache distribuído (Redis) e invalidação
- Idempotência em endpoints de escrita
- Política de retenção e conformidade LGPD por produto
- Estratégia de deploy (contêiner: onde? Azure/AWS/on-prem do cliente?)
- Roadmap público e política de suporte a versões
