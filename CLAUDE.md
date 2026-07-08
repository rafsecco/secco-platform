# Secco Platform

Monorepo da Secco Platform — ecossistema corporativo modular para .NET. Produtos adotáveis de forma independente via NuGet, com identidade arquitetural única: Clean Architecture, OpenAPI + NSwag + Scalar, multi-tenancy database-per-tenant, SDKs próprios.

## Fonte da verdade

- **`docs/adr/secco-platform-adrs.md`** — 19 ADRs ratificadas. Ler antes de qualquer decisão estrutural. **Nunca produzir código que contradiga uma ADR Aceita.** Decisão arquitetural nova (afeta 2+ produtos ou é difícil de reverter) → propor nova ADR antes de codificar; ADRs nunca são editadas retroativamente, são substituídas.
- **`docs/roadmap.md`** — fase atual e próximos entregáveis. Atualizar os checkboxes ao concluir itens.
- **`.claude/skills/`** — regras operacionais detalhadas (`secco-platform-standards` e `secco-db-naming`). Seguir sempre.

## Estado atual

Fases 0, 1 e 2 concluídas (ADRs ratificadas, fundação do monorepo, CI, `Secco.SharedKernel`). **Fase 3 em andamento: `Secco.SDK`** — começando por `AddSeccoCorrelation()`.

## Segurança — critério transversal obrigatório (ADR-0020)

Toda análise de design e toda revisão de código avalia explicitamente, antes de implementar: confiança em input externo (headers, payloads — nunca propagar/persistir sem validar formato/tamanho), injeção (SQL, log forging, header injection), vazamento de informação (erros, logs, stack traces em produção), isolamento de tenant, autenticação/autorização explícitas, negação de serviço (limites de tamanho/taxa em input não confiável), e dependências novas (manutenção ativa, CVEs conhecidas). Ao propor decisões de design, apresentar o risco de segurança de cada opção — não só o trade-off funcional.

## Comandos

```bash
dotnet restore Secco.Platform.slnx
dotnet build Secco.Platform.slnx --configuration Release   # warnings = erros
dotnet test Secco.Platform.slnx
```

## Regras inegociáveis (resumo — detalhes nas skills e ADRs)

- Prefixo `Secco.*` em tudo que é novo; nada novo com `RS.` (ADR-0016).
- Clean Architecture, 4 camadas, dependências apontando para dentro; zero infraestrutura em Domain/Application (ADR-0002).
- Erros de negócio via `Result<T>` do SharedKernel, nunca exceções para fluxo (ADR-0004).
- SharedKernel só admite tipos usados por 2+ produtos, com zero dependências externas, sem I/O, estáveis — na dúvida, fica no produto (ADR-0003).
- Comunicação entre produtos exclusivamente via clients NSwag gerados (`Secco.<Produto>.Client`); nunca `HttpClient` manual; contrato mudou → regenerar `openapi.json` + client no mesmo PR (ADR-0006).
- Multi-tenancy database-per-tenant; nenhuma query cruza tenants; acesso via `ITenantConnectionFactory` (ADR-0005).
- Banco: SQL Server é o provider padrão, PostgreSQL segundo (ADR-0018); notação húngara minúscula — `tb_`, `vw_`, colunas `id_pk_`/`id_fk_`/`id_pfk_`, `ds_`, `dt_`, `nr_`, `ie_`, `fl_`, `vl_`, `qt_` — via convention global do EF Core, nunca digitada à mão (ADR-0017); `sp_` mantido por decisão registrada.
- Seed: referência (todos os ambientes, idempotente) vs desenvolvimento (só DEV, guarda dupla `IsDevelopment()` + flag) — nunca misturar (ADR-0019).
- Background: nativo → Hangfire/SQL Server via `IBackgroundJobScheduler` do SDK → broker só com ADR nova (ADR-0015).
- Testes: xUnit + FluentAssertions **v7 fixado** (v8+ é licença comercial) + NSubstitute + Testcontainers (MsSql padrão); toda feature acompanha testes no mesmo PR (ADR-0012).
- Central Package Management: versões só em `Directory.Packages.props`; `PackageReference` sem `Version` nos csproj (ADR-0011).
- Commits: Conventional Commits (`feat(logstream): ...`).

## Checklist antes de concluir qualquer entrega

Executar o checklist da skill `secco-platform-standards` (ADRs respeitadas, camadas corretas, Result pattern, contratos regenerados, isolamento de tenant, análise de segurança ADR-0020, testes incluídos) e atualizar `docs/roadmap.md` se algum item de fase foi concluído.
