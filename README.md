# Secco Platform

Plataforma corporativa modular para .NET. Cada produto é adotável de forma independente via NuGet, compartilhando a mesma identidade arquitetural: Clean Architecture, OpenAPI + NSwag + Scalar, multi-tenancy database-per-tenant e SDKs próprios.

| Produto | Descrição | Pacote / status |
|---|---|---|
| [Secco.SharedKernel](src/SharedKernel/Secco.SharedKernel/README.md) | Primitivas compartilhadas (Result, paginação, entidades base, claims/permissions) | `Secco.SharedKernel` **0.3.2** |
| [Secco.SDK.AspNetCore](src/SDK/Secco.SDK.AspNetCore/README.md) | Cross-cutting de runtime (auth, correlation, tenancy, health, resiliência, autorização, OpenAPI) | `Secco.SDK.AspNetCore` **0.4.1** |
| [Secco.SDK.EntityFrameworkCore](src/SDK/Secco.SDK.EntityFrameworkCore/README.md) | `SeccoDbContext`, nomenclatura de banco por convention, seeding | `Secco.SDK.EntityFrameworkCore` **0.2.0** |
| [Secco.SDK.Testing](src/SDK/Secco.SDK.Testing/README.md) | Base das factories de teste de integração (SQL Server real, tokens, tenancy/permissões) | `Secco.SDK.Testing` (pronto para publicar) |
| [Secco.LogStream](src/LogStream/README.md) | Logging & Observability (produto de referência) | Disponível · client `Secco.LogStream.Client` **0.1.1** |
| [Secco.SecureGate](src/SecureGate/README.md) | Identity & Access Management: OIDC (client credentials + login de usuário + federação Entra ID), catálogo de tenants, autorização Role+Permission | Disponível · client `Secco.SecureGate.Client` **0.2.1** |
| [Secco.AdminPortal](src/AdminPortal/README.md) | Console de operação (Blazor Server, relying party OIDC) | Disponível (aplicação, não pacote) |
| [Secco.NotificationHub](src/NotificationHub/README.md) | Envio de notificações multi-canal (e-mail + inbox in-app) | Disponível · client `Secco.NotificationHub.Client` (pronto para publicar) |
| [Secco.Templates](templates/README.md) | `dotnet new secco-service` | `Secco.Templates` **0.1.0** |

## Começando

Consome a plataforma? Veja o **[guia de início](docs/getting-started.md)** — configurar o feed do GitHub Packages, instalar os pacotes, subir uma API com `AddSeccoPlatform()`, gerar um produto pelo template e consumir os clients.

```bash
# feed privado (ADR-0011) + instalar o SDK
dotnet nuget add source "https://nuget.pkg.github.com/rafsecco/index.json" --name secco \
  --username <usuario> --password <PAT read:packages> --store-password-in-clear-text
dotnet add package Secco.SDK.AspNetCore
```

## Documentação

- **[Guia de início](docs/getting-started.md)** — para quem **adota** a plataforma (feed, pacotes, template, clients).
- **[Visão de arquitetura](docs/architecture-overview.md)** — os pilares e como os produtos se encaixam.
- **[Decisões arquiteturais (ADRs)](docs/adr/secco-platform-adrs.md)** — fonte da verdade; nenhum código contradiz uma ADR Aceita.
- **[Roadmap](docs/roadmap.md)** — fases e entregáveis.
- **[Guia de testes](docs/testing-guide.md)** — verificar a plataforma: suíte inteira, projeto por projeto, o template e o roteiro manual.
- **Por produto** — cada produto/pacote tem README próprio (links na tabela acima).

## Build & testes (contribuidores)

```bash
dotnet restore Secco.Platform.slnx
dotnet build Secco.Platform.slnx --configuration Release   # warnings = erros
dotnet test Secco.Platform.slnx                            # integração usa Testcontainers (Docker)
```

São **525 testes** em 8 projetos. Máquina com Docker apertado, execução por projeto, validação do template e roteiro de teste manual: veja o **[guia de testes](docs/testing-guide.md)**.

### Ambiente local

O monorepo abre no VS Code pronto para `F5`. Cada API tem duas configurações de auth de desenvolvimento: **standalone** (HS256, o produto sozinho) e **federado** (RS256 emitido pelo SecureGate — o único modo em que o AdminPortal funciona). As tasks cobrem build, testes, regeneração de contrato + client NSwag e `ef migrations add`.

A infraestrutura vem de um `docker-compose.yml` único na raiz, com profiles:

```bash
docker compose up -d                          # só a infra (SQL Server, MailHog) — é o que o F5 usa
docker compose --profile logstream up -d      # um produto em container
docker compose --profile all up -d            # os três produtos, com um SQL Server só
docker compose --profile postgres up -d       # PostgreSQL, quando for exercitar o 2º provider
```

Convenção de portas: APIs a partir de **4001**, serviços de tela a partir de **5001**, HTTP = HTTPS + 100.

> Máquinas com Docker apertado podem não aguentar as suítes de integração em paralelo (cada uma sobe o próprio SQL Server via Testcontainers). Nesse caso, rode `dotnet test` projeto a projeto.
