# Secco Platform

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=flat-square)
![EF Core](https://img.shields.io/badge/EF%20Core-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Blazor](https://img.shields.io/badge/Blazor%20Server-512BD4?style=flat-square&logo=blazor&logoColor=white)
![SQL Server](https://img.shields.io/badge/SQL%20Server-CC2927?style=flat-square)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?style=flat-square&logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-2496ED?style=flat-square&logo=docker&logoColor=white)

![OpenAPI](https://img.shields.io/badge/OpenAPI-6BA539?style=flat-square&logo=openapiinitiative&logoColor=white)
![OpenIddict](https://img.shields.io/badge/OpenIddict-OIDC-0B7285?style=flat-square)
![Hangfire](https://img.shields.io/badge/Hangfire-background%20jobs-B32017?style=flat-square)
![xUnit](https://img.shields.io/badge/xUnit-5E5E5E?style=flat-square)
![Testcontainers](https://img.shields.io/badge/Testcontainers-integra%C3%A7%C3%A3o-291A38?style=flat-square)
![GitHub Actions](https://img.shields.io/badge/CI-GitHub%20Actions-2088FF?style=flat-square&logo=githubactions&logoColor=white)
![NuGet](https://img.shields.io/badge/NuGet-GitHub%20Packages-004880?style=flat-square&logo=nuget&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-750014?style=flat-square)

Plataforma corporativa modular para .NET. Cada produto é adotável de forma independente via NuGet, compartilhando a mesma identidade arquitetural: Clean Architecture, OpenAPI + NSwag + Scalar, multi-tenancy database-per-tenant e SDKs próprios.

| Produto | Descrição | Pacote / status |
|---|---|---|
| [Secco.SharedKernel](src/SharedKernel/Secco.SharedKernel/README.md) | Primitivas compartilhadas (Result, paginação, entidades base, claims/permissions) | `Secco.SharedKernel` **0.3.7** |
| [Secco.SDK.AspNetCore](src/SDK/Secco.SDK.AspNetCore/README.md) | Cross-cutting de runtime (auth, correlation, tenancy, health, resiliência, autorização, OpenAPI) | `Secco.SDK.AspNetCore` **0.7.0** |
| [Secco.SDK.EntityFrameworkCore](src/SDK/Secco.SDK.EntityFrameworkCore/README.md) | `SeccoDbContext`, nomenclatura de banco por convention, seeding e cifragem de segredo em repouso (ADR-0025) | `Secco.SDK.EntityFrameworkCore` **0.4.0** |
| [Secco.SDK.ClientCredentials](src/SDK/Secco.SDK.ClientCredentials/README.md) | Client credentials OAuth 2 entre produtos (token de máquina, pacote fino) | `Secco.SDK.ClientCredentials` **0.1.0** |
| [Secco.SDK.Logging](src/SDK/Secco.SDK.Logging/README.md) | Sink `ILogger` → LogStream (`AddLogStream()`, ADR-0008) | `Secco.SDK.Logging` **0.2.0** |
| [Secco.SDK.Testing](src/SDK/Secco.SDK.Testing/README.md) | Base das factories de teste de integração (SQL Server real, tokens, tenancy/permissões) | `Secco.SDK.Testing` **0.1.0** |
| [Secco.LogStream](src/LogStream/README.md) | Logging & Observability (produto de referência) | Disponível · client `Secco.LogStream.Client` **0.4.0** |
| [Secco.SecureGate](src/SecureGate/README.md) | Identity & Access Management: OIDC (client credentials + login de usuário + federação Entra ID), catálogo de tenants, autorização Role+Permission | Disponível · client `Secco.SecureGate.Client` **0.4.0** |
| [Secco.AdminPortal](src/AdminPortal/README.md) | Console de operação (Blazor Server, relying party OIDC) | Disponível (aplicação, não pacote) |
| [Secco.NotificationHub](src/NotificationHub/README.md) | Envio de notificações multi-canal (e-mail + inbox in-app) | Disponível · client `Secco.NotificationHub.Client` **0.6.0** |
| [Secco.Templates](templates/README.md) | `dotnet new secco-service` | `Secco.Templates` **0.2.0** |

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
- **[Changelog](CHANGELOG.md)** — mudanças por pacote publicável (ADR-0011).
- **[Como contribuir](CONTRIBUTING.md)** — o que não se negocia, convenções e o checklist antes do PR.
- **[Política de segurança](SECURITY.md)** — escopo e canal privado para reportar vulnerabilidade.

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
