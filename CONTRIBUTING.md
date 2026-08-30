# Contribuindo

Este arquivo é um roteador, não um manual: as regras vivem nas ADRs, e duplicá-las aqui só criaria uma segunda fonte para divergir da primeira.

## A regra zero

**[`docs/adr/secco-platform-adrs.md`](docs/adr/secco-platform-adrs.md) é a fonte da verdade.** Nenhum código contradiz uma ADR Aceita. Se a tarefa parece exigir isso, o caminho não é contornar — é propor uma ADR nova que substitua a antiga. ADRs não são editadas retroativamente.

Decisão que afeta dois ou mais produtos, ou que é difícil de reverter, precisa de ADR **antes** do código.

## Antes de abrir o editor

| Você quer | Leia |
|---|---|
| Entender como as peças se encaixam | [`docs/architecture-overview.md`](docs/architecture-overview.md) |
| Saber o que está feito e o que vem | [`docs/roadmap.md`](docs/roadmap.md) |
| Rodar e testar a plataforma | [`docs/testing-guide.md`](docs/testing-guide.md) |
| Consumir a plataforma num produto seu | [`docs/getting-started.md`](docs/getting-started.md) |
| Saber por que uma alternativa foi descartada | [`docs/design-decisions-log.md`](docs/design-decisions-log.md) |

As regras operacionais detalhadas estão em [`.claude/skills/`](.claude/skills/) — `secco-platform-standards` (padrões e checklist), `secco-db-naming` (nomenclatura de banco, ADR-0017) e `secco-platform-release` (publicação de pacote).

## O que não se negocia

Um resumo do que mais tropeça na prática. O detalhe está nas ADRs citadas.

- **Prefixo `Secco.*`** em tudo que é novo (ADR-0016).
- **Quatro camadas**, dependências apontando para dentro, zero infraestrutura em Domain e Application (ADR-0002). Isso inclui abstração de opções: a Application recebe POCO, não `IOptions<T>`.
- **`Result<T>`** para erro de negócio, nunca exceção para controle de fluxo (ADR-0004).
- **Comunicação entre produtos só por client NSwag gerado** (ADR-0006). Mudou o contrato → `openapi.json` e client regenerados **no mesmo PR**, senão o CI falha.
- **Nenhuma query cruza tenants** (ADR-0005). Se uma feature parece exigir isso, provavelmente é dado de plataforma, não de tenant — pare e discuta.
- **Todo endpoint tem permissão** (`RequireAuthorization("recurso:acao")`, ADR-0021). Sem exceção, incluindo o recurso de exemplo do template.
- **Nomenclatura de banco pela convention**, nunca digitada à mão (ADR-0017).
- **Toda feature acompanha teste no mesmo PR**; bug corrigido ganha teste que o reproduz (ADR-0012).
- **Versões de pacote só em `Directory.Packages.props`** (ADR-0011).

## Segurança é parte do design

Antes de implementar, e não na revisão, avalie explicitamente (ADR-0020): confiança em input externo, injeção, vazamento de informação, isolamento de tenant, autenticação e autorização explícitas, negação de serviço, e dependência nova (manutenção ativa, CVEs). Ao apresentar opções de design, inclua o risco de segurança de cada uma — não só o trade-off funcional.

## Produto novo

Nunca por cópia manual de outro produto: use `dotnet new secco-service` (ADR-0013). Se o template não cobre algo que as ADRs exigem, **atualizar o template faz parte da tarefa** — divergência entre ele e o padrão é bug de prioridade alta.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/pt-br/), com escopo por produto:

```
feat(logstream): ingestão em lote com limite configurável
fix(sdk): tenancy resolve antes da autorização
docs(adr): ratifica a ADR-0026
```

Prefira explicar **por que** no corpo. O histórico é a única fonte que sobrevive quando a memória de quem decidiu não está mais disponível.

## Antes de dizer que terminou

Rode o checklist da skill `secco-platform-standards`: ADRs respeitadas, camadas corretas, `Result` no lugar certo, contratos regenerados, isolamento de tenant, análise de segurança, testes incluídos. E atualize [`docs/roadmap.md`](docs/roadmap.md) se algum item de fase foi concluído.

Verificação mínima antes de abrir PR:

```bash
dotnet build Secco.Platform.slnx --configuration Release   # warnings = erros
dotnet test Secco.Platform.slnx --configuration Release --no-build
```

Mexeu no template? Ele fica fora da solution e tem fluxo próprio — veja a seção 5 do [guia de testes](docs/testing-guide.md).
