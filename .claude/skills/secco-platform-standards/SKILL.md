---
name: secco-platform-standards
description: Padrões arquiteturais, convenções e regras de desenvolvimento do ecossistema Secco Platform. Usar SEMPRE que a tarefa envolver qualquer projeto com prefixo Secco.* ou o legado RS.* (Secco.SecureGate, Secco.LogStream, Secco.NotificationHub, Secco.Configuration, Secco.FeatureFlags, Secco.Audit, Secco.AdminPortal, Secco.SharedKernel, Secco.SDK, Secco.Templates, RS.Logging, RS.LogStream), o monorepo secco-platform, ou quando o usuário mencionar "Secco Platform", "SecureGate", "LogStream", "SharedKernel", ADRs da plataforma, geração de clients NSwag, multi-tenancy database-per-tenant, criação de um novo serviço/produto da plataforma, ou um produto de operação estilo "Blazor Server", "relying party" ou "console de operação" — mesmo que peça apenas "um endpoint", "uma entidade" ou "um teste" dentro desses projetos.
---

# Secco Platform — Padrões de Desenvolvimento

Este skill garante que todo código produzido para o ecossistema Secco Platform siga as decisões arquiteturais registradas em `docs/adr/secco-platform-adrs.md` (as ADRs). As ADRs são a fonte da verdade; este skill é o resumo operacional.

## Regra zero: ADRs primeiro

1. Se o repositório estiver acessível, ler `docs/adr/secco-platform-adrs.md` antes de decisões estruturais.
2. Nunca produzir código que contradiga uma ADR **Aceita**. Se a tarefa pedida contradiz uma ADR, avisar o usuário e propor: (a) ajustar a tarefa, ou (b) redigir uma nova ADR que substitua a antiga.
3. Decisão nova que afete mais de um produto ou seja difícil de reverter → propor o texto da ADR (template no próprio documento) antes de codificar.
4. Não reabrir decisões já aceitas sem motivo novo — o objetivo das ADRs é justamente evitar re-discussão a cada sessão.

## Segurança é parte do design, não revisão posterior (ADR-0020)

Antes de implementar, avaliar: confiança em input externo (validar formato/tamanho de tudo que vem de fora do processo — headers, payloads, mensagens), injeção (SQL, log forging, header injection), vazamento de informação (erros/logs/stack traces em produção), isolamento de tenant ("este código pode vazar ou aceitar dado de outro tenant?"), autenticação/autorização explícitas, negação de serviço (limites em input não confiável), dependências novas (manutenção ativa, CVEs). Ao apresentar opções de design, incluir o risco de segurança de cada uma.

## Prefixo e migração (ADR-0016)

O prefixo oficial é `Secco.*`. Nenhum código novo usa `RS.`. Ao tocar em código legado com prefixo `RS.` (ex.: RS.Logging), aproveitar a tarefa para propor a renomeação para `Secco.*` quando o escopo permitir.

## Estrutura obrigatória de um produto

Todo produto tem quatro camadas com dependências apontando para dentro:

```
Secco.<Produto>.Api            → Application, Infrastructure (composição)
Secco.<Produto>.Application    → Domain, Secco.SharedKernel
Secco.<Produto>.Domain         → Secco.SharedKernel (apenas)
Secco.<Produto>.Infrastructure → Application, Domain
Secco.<Produto>.Client         → GERADO por NSwag; nunca editado à mão
```

Proibições que valem sempre:
- Domain e Application não referenciam EF Core, HTTP, nem qualquer pacote de infraestrutura.
- Nenhum `HttpClient` manual entre serviços da plataforma — comunicação sempre via `Secco.<Produto>.Client` gerado.
- Nenhum produto grava logs em banco próprio — logging via `AddLogStream()` do SDK.
- Cross-cutting (auth, correlation, tenancy, retry, health) vem do `Secco.SDK`, nunca é reimplementado localmente.
- Background processing segue as camadas da ADR-0015: nativo (`BackgroundService`) só para manutenção in-process sem persistência; precisou de persistência/retry/visibilidade → Hangfire com storage SQL Server, sempre via abstração `IBackgroundJobScheduler` do SDK (nunca acoplar produto ao Hangfire); mensageria com broker exige ADR nova.

### Exceção: produtos de operação / relying party (ADR-0023)

Nem todo produto é uma API de 4 camadas. Um **console de operação** (ex.: `Secco.AdminPortal`) é uma exceção legítima, não um desvio a corrigir:

- Sem Domain nem Infrastructure próprios, sem banco próprio — orquestra os demais produtos via `Secco.<Produto>.Client` gerados (ADR-0006), não tem estado de negócio para modelar.
- É **relying party OIDC**, não resource server: autentica via cookie de sessão + authorization code/PKCE contra o SecureGate. Usa `AddSeccoAuthentication()`? **Não** — essa extensão é para produtos que validam JWT como resource server; um relying party reusa só o cross-cutting não-de-auth do SDK (correlation, resilience, health).
- Chama os demais produtos **on-behalf-of** o operador: o token do próprio operador (custodiado na sessão, anexado via algo como `IOperatorTokenProvider`) vai em toda chamada — nunca client credentials do produto de operação em nome próprio. A auditoria é a pessoa, não o console.
- Telas/ações são protegidas por `RequireRole` (ou policy equivalente) no papel de operador, não por scope de API.

Referência concreta: `Secco.AdminPortal` (Blazor Server, ADR-0023/0024) — ver `src/AdminPortal/README.md`. Um produto novo nesse molde documenta a exceção citando a ADR-0023, sem inventar Domain/Infrastructure vazios só para "bater" com a estrutura padrão.

## SharedKernel: critério de admissão

Antes de adicionar qualquer tipo ao `Secco.SharedKernel`, verificar as quatro condições (todas obrigatórias): usado por 2+ produtos; zero dependências além da BCL; sem I/O e sem estado; interface estável. Em caso de dúvida, o código fica no produto. `TenantContext` com lógica, middlewares e qualquer coisa com dependência externa pertencem ao `Secco.SDK`.

## Padrões de código

### Result Pattern
Fluxo de erro de negócio usa `Result<T>` / `Error` do SharedKernel — nunca exceções para controle de fluxo. Exceções ficam reservadas a falhas de infraestrutura e bugs.

```csharp
public async Task<Result<LogEntryDto>> Handle(CreateLogEntryCommand cmd, CancellationToken ct)
{
    var tenant = _tenantContext.Current;
    if (tenant is null)
        return Result.Failure<LogEntryDto>(PlatformErrors.Tenant.NotResolved);
    // ...
    return Result.Success(dto);
}
```

Na API, `Result` é convertido para HTTP via extensão padrão (`ToActionResult()` / `ToHttpResult()`), com erros no formato ProblemDetails.

### API
- Rotas: `/api/v1/<recurso-kebab-plural>`; JSON camelCase; erros em ProblemDetails.
- Paginação sempre com `PageRequest` / `PagedResult<T>` do SharedKernel.
- Header `X-Correlation-Id` aceito e propagado (o SDK cuida disso — não reimplementar).
- Todo endpoint documentado no OpenAPI (summary + response types); Scalar como UI.
- OpenAPI sempre via `AddSeccoOpenApi()` do SDK — nunca `AddOpenApi()` cru. Motivo: sem o schema transformer da plataforma, enums serializados como string saem do `AddOpenApi()` puro sem `type: string` no schema, e o NSwag gera o client com enum numérico — quebra a desserialização no consumidor (bug real, já corrigido uma vez no contrato do LogStream).
- Composição de cross-cutting via `AddSeccoPlatform()` e extensões `AddSecco*()` do SDK.

### Multi-tenancy (Database per Tenant)
- Tenant resolvido por claim do token (primário) ou header `X-Tenant-Id` — via SDK.
- Acesso a dados sempre através do `ITenantConnectionFactory`; jamais connection string fixa de tenant.
- Nenhuma query cruza dados de tenants. Se uma feature parecer exigir isso, parar e discutir (provavelmente é dado de plataforma, não de tenant).

### Nomenclatura (resumo)
Projetos/namespaces `Secco.<Produto>.<Camada>`; banco de dados em notação húngara minúscula conforme ADR-0017 e skill `secco-db-naming` (`tb_`, `vw_`; colunas `id_pk_`/`id_fk_`/`id_pfk_`, `ds_`, `dt_`, `nr_`, `ie_`, `fl_`, `vl_`, `qt_` — via convention global do EF Core, nunca digitado à mão); handlers `VerboSubstantivoHandler`; testes `Metodo_Cenario_Resultado`; commits em Conventional Commits (`feat(logstream): ...`).

## Seed de dados (ADR-0019)

Duas categorias, nunca misturadas:
- **Referência (todos os ambientes):** valores obrigatórios de enums dinâmicos, registros de sistema. Idempotente, IDs determinísticos, integra o provisionamento de cada tenant e reexecuta após migrations.
- **Desenvolvimento (apenas DEV):** dados de amostra para navegar na aplicação. Guarda dupla obrigatória (`IsDevelopment()` + flag `Secco:Seed:Development`); executa após o seed de referência; dados via Bogus locale pt_BR com seed randômico fixo.

Local: `Infrastructure/Seeding/` com `ReferenceDataSeeder` e `DevelopmentDataSeeder`. Ao criar enum dinâmico novo, atualizar o seed de referência no mesmo PR (+ teste de integração validando os valores obrigatórios).

## Contratos e clients NSwag

Ao alterar qualquer contrato de API:
1. Atualizar o endpoint e o OpenAPI.
2. Regenerar o `openapi.json` versionado no repo e o projeto `Secco.<Produto>.Client` **no mesmo PR**.
3. Se a mudança for breaking: nova versão de API (`/v2`) conforme ADR-0009, major no pacote client, entrada no CHANGELOG.
4. Nunca editar código dentro de `Secco.<Produto>.Client` manualmente — ajustes vão na configuração do NSwag ou em partial classes fora da pasta gerada.

## Testes

- Unit para Domain/Application (sem infraestrutura); Integration com Testcontainers + `WebApplicationFactory` para Infrastructure/Api.
- Stack: xUnit + FluentAssertions + NSubstitute.
- Toda feature nova acompanha testes no mesmo PR. Bug corrigido → teste que o reproduz.

## Novo produto ou serviço

Nunca criar por cópia manual de outro produto. Usar `dotnet new secco-service` (Secco.Templates). Se o template ainda não cobrir algo exigido pelas ADRs, atualizar o template como parte da tarefa.

## Checklist antes de concluir qualquer entrega

- [ ] Nenhuma ADR Aceita foi violada (em dúvida, citar a ADR relevante na resposta).
- [ ] Prefixo `Secco.*` em tudo que é novo; nada novo com `RS.`.
- [ ] Dependências entre camadas respeitadas; nada de infra em Domain/Application.
- [ ] Erros de negócio via `Result<T>`; HTTP em ProblemDetails.
- [ ] Contrato mudou? `openapi.json` e Client regenerados no mesmo PR.
- [ ] Nada foi adicionado ao SharedKernel sem passar no critério de admissão.
- [ ] Queries respeitam isolamento de tenant.
- [ ] Análise de segurança feita (ADR-0020): input externo validado, sem injeção/vazamento, tenant isolado, auth explícita, limites contra DoS.
- [ ] Testes incluídos; nomenclatura e commits nas convenções.
- [ ] Se surgiu decisão arquitetural nova: ADR proposta ao usuário.
- [ ] Produto novo é de operação/relying party (sem domínio/banco próprios)? Seguir a exceção da ADR-0023 — não inventar camadas fictícias.
- [ ] Endpoint HTTP novo usa `AddSeccoOpenApi()` (nunca `AddOpenApi()` cru)?
