# Secco Platform

Plataforma corporativa modular para .NET. Cada produto é adotável de forma independente via NuGet, compartilhando a mesma identidade arquitetural: Clean Architecture, OpenAPI + NSwag + Scalar, multi-tenancy database-per-tenant e SDKs próprios.

| Produto | Descrição | Pacote / status |
|---|---|---|
| [Secco.SharedKernel](src/SharedKernel/Secco.SharedKernel/README.md) | Primitivas compartilhadas (Result, paginação, entidades base, claims/permissions) | `Secco.SharedKernel` **0.3.0** |
| [Secco.SDK.AspNetCore](src/SDK/Secco.SDK.AspNetCore/README.md) | Cross-cutting de runtime (auth, correlation, tenancy, health, resiliência, autorização, OpenAPI) | `Secco.SDK.AspNetCore` **0.3.0** |
| [Secco.SDK.EntityFrameworkCore](src/SDK/Secco.SDK.EntityFrameworkCore/README.md) | `SeccoDbContext`, nomenclatura de banco por convention, seeding | `Secco.SDK.EntityFrameworkCore` **0.2.0** |
| [Secco.LogStream](src/LogStream/README.md) | Logging & Observability (produto de referência) | Disponível · client `Secco.LogStream.Client` **0.1.1** |
| [Secco.SecureGate](src/SecureGate/README.md) | Identity & Access Management: OIDC (client credentials + login de usuário), catálogo de tenants, autorização Role+Permission | Disponível · client `Secco.SecureGate.Client` **0.1.0** |
| [Secco.AdminPortal](src/AdminPortal/README.md) | Console de operação (Blazor Server, relying party OIDC) | Disponível (aplicação, não pacote) |
| [Secco.NotificationHub](src/NotificationHub/README.md) | Envio de notificações (e-mail no v1) | Disponível |
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
- **Por produto** — cada produto/pacote tem README próprio (links na tabela acima).

## Build & testes (contribuidores)

```bash
dotnet restore Secco.Platform.slnx
dotnet build Secco.Platform.slnx --configuration Release   # warnings = erros
dotnet test Secco.Platform.slnx                            # integração usa Testcontainers (Docker)
```
