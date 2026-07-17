# Secco.NotificationHub

Canal único de notificações da Secco Platform (Fase 8): os demais produtos pedem "notifique este contato sobre Y" sem saber como o envio é feito. Nasceu do template `dotnet new secco-service` (ADR-0013) — 4 camadas (ADR-0002), `AddSeccoPlatform()` (ADR-0004), multi-tenancy database-per-tenant (ADR-0005), contrato OpenAPI versionado com client NSwag (ADR-0006), nomenclatura de banco por convention (ADR-0017), dois providers (ADR-0018) e testes (ADR-0012).

## Estado atual

**8.1 concluída** — fundação gerada pelo template, projetos na solution, migrations iniciais (SQL Server + PostgreSQL), contrato OpenAPI real (endpoints do recurso Sample) com teste de contrato, `Secco.NotificationHub.Client` compilando a partir do contrato, path filter no CI (`build-notificationhub`). O recurso **Sample** segue presente como referência executável dos padrões — será removido na 8.2, quando o domínio real (envio de e-mail) entra.

## Escopo decidido para o v1 (Fase 8, ver `docs/roadmap.md`)

- **Só canal e-mail** — abstração de canal interna pronta para um 2º canal futuro, sem prever a forma dele.
- **Sem motor de templates** — o chamador manda assunto/corpo prontos; centralizar templates é extensão aditiva se 2+ produtos repetirem a mesma necessidade.
- **Sem acoplamento ao SecureGate** — o chamador resolve/informa o e-mail do destinatário diretamente; o NotificationHub não conhece o modelo de usuário de outro produto.
- **Envio assíncrono via Hangfire** (`IBackgroundJobScheduler`, ADR-0015) com retry automático em falha transitória do provider (SMTP/SendGrid).
- **Status consultável** (`GET /api/v1/notifications/{id}` → `Pending`/`Sent`/`Failed` + motivo).
- **Broker de mensageria continua adiado** — a Camada 3 da ADR-0015, reservada para este produto, só abre quando um caso real de multi-produtor/consumidor aparecer.

## O recurso Sample demonstra (temporário — ver 8.2)

- Entidade `BaseEntity` (Guid v7) com guarda de invariante e colunas por convention (`tb_samples`, `ds_name`...).
- Handler com `Result<T>` (ADR-0004) e limites de entrada (ADR-0020) via options com bind lazy.
- Endpoints protegidos pela `FallbackPolicy`, `ToHttpResult()` → ProblemDetails, paginação `PagedResult<T>`.
- Testes unitários (fake da porta) e de integração com SQL Server real (Testcontainers), incluindo isolamento físico entre tenants e teste de contrato OpenAPI.
