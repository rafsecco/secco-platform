# Changelog

Mudanças relevantes dos pacotes publicáveis da Secco Platform.

A ADR-0011 exige **entrada de changelog por pacote** em toda breaking change. Como o repositório é um monorepo, as entradas ficam neste arquivo único, com **uma seção por pacote** — a exigência é que a mudança do pacote esteja registrada, não que exista um arquivo por pacote.

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). Versionamento SemVer estrito por pacote (ADR-0011): a versão sai da tag git via MinVer, nunca de bump manual em csproj.

> **Histórico anterior a 2026-08-30.** Este arquivo nasceu depois das primeiras releases. As versões já publicadas estão listadas com data, que é verificável pela tag git, mas **sem descrição retroativa** — reconstruí-las a posteriori produziria texto plausível e não confiável. O contexto real de cada fase está em [`docs/roadmap.md`](docs/roadmap.md). Entradas descritivas valem a partir daqui.

---

## Não publicado

### Secco.SDK.EntityFrameworkCore

- **Adicionado** `SeccoDatabaseProviders`: seleção de provider de banco por receita. O produto declara o que aplicar (incluindo o assembly de migrations), o SDK apenas seleciona — **sem nenhuma dependência de engine adicionada ao pacote**, preservando a cláusula de extensibilidade da ADR-0018.

---

## Publicado

### Secco.SharedKernel

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

#### 0.1.0 — 2026-08-30

Primeira versão. Client NSwag gerado do `openapi.json` versionado do NotificationHub (ADR-0006) — a única forma legítima de outro produto da plataforma falar com ele. Cobre o despacho multi-canal, a consulta de status e os endpoints de inbox in-app.

### Secco.Templates

| Versão | Data |
|---|---|
| 0.1.0 | 2026-07-14 |
