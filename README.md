# Secco Platform

Plataforma corporativa modular para .NET. Cada produto é adotável de forma independente via NuGet, compartilhando a mesma identidade arquitetural: Clean Architecture, OpenAPI + NSwag + Scalar, multi-tenancy database-per-tenant e SDKs próprios.

| Produto | Descrição | Status |
|---|---|---|
| [Secco.SharedKernel](src/SharedKernel/Secco.SharedKernel/README.md) | Primitivas compartilhadas (Result, paginação, entidades base) | v0.1 publicado |
| [Secco.SDK.AspNetCore](src/SDK/Secco.SDK.AspNetCore/README.md) | Cross-cutting de runtime (auth, correlation, tenancy, health, resiliência) | v0.1 publicado |
| [Secco.SDK.EntityFrameworkCore](src/SDK/Secco.SDK.EntityFrameworkCore/README.md) | `SeccoDbContext`, nomenclatura de banco por convention, seeding | v0.1 publicado |
| [Secco.LogStream](src/LogStream/README.md) | Logging & Observability | Em desenvolvimento (Fase 4) |
| Secco.SecureGate | Identity & Access Management (OIDC) | Planejado (Fase 6) |
| Secco.AdminPortal | Portal administrativo da plataforma | Planejado (Fase 7) |
| Secco.Templates | `dotnet new secco-service` | Planejado (Fase 5) |

## Documentação

- **Decisões arquiteturais:** [`docs/adr/secco-platform-adrs.md`](docs/adr/secco-platform-adrs.md) — fonte da verdade; nenhum código contradiz uma ADR Aceita.
- **Roadmap:** [`docs/roadmap.md`](docs/roadmap.md)
- **Produtos:** cada produto/pacote tem README próprio — links na tabela acima.
