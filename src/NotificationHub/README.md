# Secco.NotificationHub

Canal único de notificações da Secco Platform (Fase 8): os demais produtos pedem "notifique este contato sobre Y" sem saber como o envio é feito. Nasceu do template `dotnet new secco-service` (ADR-0013) — 4 camadas (ADR-0002), `AddSeccoPlatform()` (ADR-0004), multi-tenancy database-per-tenant (ADR-0005), contrato OpenAPI versionado com client NSwag (ADR-0006), nomenclatura de banco por convention (ADR-0017), dois providers (ADR-0018) e testes (ADR-0012).

## O que está entregue

### v1 — Envio de e-mail (8.1/8.2)

Escopo decidido: só canal e-mail (abstração interna pronta para um 2º canal futuro, sem prever a forma); sem motor de templates (o chamador manda assunto/corpo prontos); sem acoplamento ao SecureGate (o chamador resolve/informa o e-mail do destinatário diretamente); broker de mensageria continua adiado (ADR-0015 Camada 3, reservada para este produto, só abre com um caso real de multi-produtor/consumidor).

| Endpoint | Efeito |
|---|---|
| `POST /api/v1/notifications` | Valida limites (ADR-0020) e formato do e-mail, persiste `Pending` e enfileira o envio. Scope `notifications:write` |
| `GET /api/v1/notifications/{id}` | Consulta o status (`Pending`/`Sent`/`Failed` + motivo da falha). Scope `notifications:read` |

- **Envio assíncrono com retry** (ADR-0015 Camada 2): o SDK ganhou `IBackgroundJobScheduler`/`AddSeccoBackgroundJobs()` — Hangfire com storage SQL Server no **banco de plataforma** (nunca por tenant, seção `NotificationHub:BackgroundJobs`), `TenantJobRunner` restaura o tenant no escopo automaticamente antes do job rodar. `SendEmailJob` usa MailKit (SMTP) — `System.Net.Mail.SmtpClient` está obsoleto.
- **Falha e retry**: uma tentativa que falha marca `Failed` com o motivo (truncado) e relança a exceção — quem decide o retry é o Hangfire (`[AutomaticRetry(Attempts = 5)]`, fixado no runner do SDK). Uma tentativa seguinte bem-sucedida sobrescreve o status para `Sent`. **Simplificação consciente do v1**: enquanto há retry pendente, o status pode aparecer `Failed` transitoriamente em vez de "ainda tentando" — aceitável para o escopo atual, candidato a revisão se virar um problema real de UX.
- **Conteúdo persistido**: a notificação grava destinatário/assunto/corpo (decisão consciente — o chamador já decidiu enviar esse conteúdo; sem isso, uma falha de envio não teria contexto para diagnóstico). O `GET` não devolve assunto/corpo — só status.

## Configuração

| Seção | Uso |
|---|---|
| `NotificationHub:BackgroundJobs:ConnectionString` | Banco de PLATAFORMA do Hangfire (schema próprio, gerenciado pela lib) — nunca o banco de um tenant |
| `NotificationHub:Email` | `Host`/`Port`/`UseStartTls`/`Username`/`Password`/`FromAddress`/`FromName` do provider SMTP |
| `NotificationHub:Limits` | `MaxRecipientLength`/`MaxSubjectLength`/`MaxBodyLength` (ADR-0020) |
| `NotificationHub:Database` | Engine dos bancos de tenant (`SqlServer` padrão / `Postgres`, ADR-0018) |

## Testes

`tests/NotificationHub/Secco.NotificationHub.Tests` (ADR-0012): Testcontainers reais (SQL Server) para os bancos de tenant **e** para o banco de plataforma do Hangfire — o job de envio é processado de verdade pelo Hangfire; só o SMTP é substituído por um fake (sem servidor de e-mail real em teste). Cobre: envio bem-sucedido (`Pending` → `Sent`), falha marcada com motivo, isolamento entre tenants, validação de entrada, autorização por permissão. `TenantJobRunner` também tem teste isolado no SDK (`Secco.SDK.AspNetCore.Tests`).

**Achado de teste**: múltiplas instâncias de `WebApplicationFactory` com Hangfire no mesmo processo de teste quebram — `Hangfire.Logging.LogProvider` é um bridge de log **estático por processo**; quando a primeira factory é descartada, qualquer uso posterior do Hangfire (por outra factory) lança `ObjectDisposedException` no `LoggerFactory` capturado. Resolvido com uma `ICollectionFixture` compartilhada entre as classes de teste do produto (uma única factory para todo o assembly).

## Backlog do produto

- **8.3** — segundo provider de banco (PostgreSQL), Dockerfile/compose de desenvolvimento.
- 2º canal (push/SMS/webhook) — só quando houver demanda real de um segundo canal.
- Motor de templates — só se 2+ produtos repetirem a mesma necessidade (critério do SharedKernel aplicado ao produto).
