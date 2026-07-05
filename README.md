# Secco Platform

Plataforma corporativa modular para .NET. Cada produto é adotável de forma independente via NuGet, compartilhando a mesma identidade arquitetural: Clean Architecture, OpenAPI + NSwag + Scalar, multi-tenancy database-per-tenant e SDKs próprios.

| Produto | Descrição | Status |
|---|---|---|
| Secco.SharedKernel | Primitivas compartilhadas (Result, paginação, entidades base) | Em construção |
| Secco.SDK | Cross-cutting de runtime (auth, correlation, tenancy, resiliência) | Planejado |
| Secco.LogStream | Logging & Observability | Migração planejada |
| Secco.SecureGate | Identity & Access Management (OIDC) | Planejado |
| Secco.AdminPortal | Portal administrativo da plataforma | Planejado |
| Secco.Templates | `dotnet new secco-service` | Planejado |

## Documentação

- **Decisões arquiteturais:** [`docs/adr/secco-platform-adrs.md`](docs/adr/secco-platform-adrs.md) — fonte da verdade; nenhum código contradiz uma ADR Aceita.
- **Roadmap:** [`docs/roadmap.md`](docs/roadmap.md)
