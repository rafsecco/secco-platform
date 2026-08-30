# Changelog

Mudanças relevantes dos pacotes publicáveis da Secco Platform.

A ADR-0011 exige **entrada de changelog por pacote** em toda breaking change. Como o repositório é um monorepo, as entradas ficam neste arquivo único, com **uma seção por pacote** — a exigência é que a mudança do pacote esteja registrada, não que exista um arquivo por pacote.

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). Versionamento SemVer estrito por pacote (ADR-0011): a versão sai da tag git via MinVer, nunca de bump manual em csproj.

> **Histórico anterior a 2026-08-30.** Este arquivo nasceu depois das primeiras releases. As versões já publicadas estão listadas com data, que é verificável pela tag git, mas **sem descrição retroativa** — reconstruí-las a posteriori produziria texto plausível e não confiável. O contexto real de cada fase está em [`docs/roadmap.md`](docs/roadmap.md). Entradas descritivas valem a partir daqui.

---

## Não publicado

### Secco.SDK.Testing

Primeira versão, ainda sem tag. Base compartilhada das factories de teste de integração (ADR-0027).

- `SeccoApiFactory<TProgram>` com `ConfigureWebHost` selado e extensão por hooks (`ConfigureTestConfiguration`, `ConfigureTestServices`, `OnInitializedAsync`).
- Instância de SQL Server isolada por suíte, com override por `SECCO_TEST_SQLSERVER` para máquinas onde N containers saturam o Docker.
- Chave de assinatura aleatória por instância — nunca constante embutida (ADR-0020).
- Nome de database validado por allowlist antes de `CREATE`/`DROP DATABASE`.
- Marcado `DevelopmentDependency`: não flui transitivamente para quem referencia o produto.

### Secco.NotificationHub.Client

Primeira versão, ainda sem tag. Client NSwag do NotificationHub, gerado a partir do `openapi.json` versionado (ADR-0006).

### Secco.SDK.EntityFrameworkCore

- **Adicionado** `SeccoDatabaseProviders`: seleção de provider de banco por receita. O produto declara o que aplicar (incluindo o assembly de migrations), o SDK apenas seleciona — **sem nenhuma dependência de engine adicionada ao pacote**, preservando a cláusula de extensibilidade da ADR-0018.

---

## Publicado

### Secco.SharedKernel

| Versão | Data |
|---|---|
| 0.3.2 | 2026-07-19 |
| 0.3.1 | 2026-07-19 |
| 0.3.0 | 2026-07-14 |
| 0.2.0 | 2026-07-12 |
| 0.1.1 | 2026-07-11 |
| 0.1.0 | 2026-07-08 |

### Secco.SDK.AspNetCore

| Versão | Data |
|---|---|
| 0.4.1 | 2026-07-19 |
| 0.4.0 | 2026-07-19 |
| 0.3.0 | 2026-07-14 |
| 0.2.0 | 2026-07-12 |
| 0.1.0 | 2026-07-11 |

### Secco.SDK.EntityFrameworkCore

| Versão | Data |
|---|---|
| 0.2.0 | 2026-07-12 |
| 0.1.0 | 2026-07-11 |

### Secco.LogStream.Client

| Versão | Data |
|---|---|
| 0.1.1 | 2026-07-14 |
| 0.1.0 | 2026-07-12 |

### Secco.SecureGate.Client

| Versão | Data |
|---|---|
| 0.2.1 | 2026-07-19 |
| 0.2.0 | 2026-07-19 |
| 0.1.0 | 2026-07-14 |

### Secco.Templates

| Versão | Data |
|---|---|
| 0.1.0 | 2026-07-14 |
