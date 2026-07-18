# Secco.NotificationHub

Canal único de notificações da Secco Platform (Fase 8): os demais produtos pedem "notifique este contato/usuário sobre Y" sem saber como o envio é feito, e decidem os canais por chamada. Nasceu do template `dotnet new secco-service` (ADR-0013) — 4 camadas (ADR-0002), `AddSeccoPlatform()` (ADR-0004), multi-tenancy database-per-tenant (ADR-0005), contrato OpenAPI versionado com client NSwag (ADR-0006), nomenclatura de banco por convention (ADR-0017), dois providers (ADR-0018) e testes (ADR-0012).

## O que está entregue

### v1 — Despacho multi-canal: e-mail + in-app (Fase 8 completa)

Escopo decidido: e-mail (assíncrono, com retry) e inbox in-app (gravação imediata) num único ponto de despacho — quem chama decide os `channels` por requisição; sem motor de templates (o chamador manda título/mensagem prontos); sem acoplamento ao SecureGate (o chamador resolve/informa `userId`/`recipient` diretamente — o Hub nunca busca contato de ninguém); `Source`/`Type` são texto livre, nunca um enum fechado — um produto novo que precise notificar algo não exige alterar o Hub; broker de mensageria continua adiado (ADR-0015 Camada 3, reservada para este produto, só abre com um caso real de multi-produtor/consumidor).

| Endpoint | Efeito |
|---|---|
| `POST /api/v1/notifications` | Despacha para 1+ canais (`email`, `in_app`) numa chamada só. Valida limites (ADR-0020) e formato do e-mail quando solicitado; devolve os IDs criados por canal. Scope `notifications:write` |
| `GET /api/v1/notifications/{id}` | Consulta o status de uma notificação por e-mail (`Pending`/`Sent`/`Failed` + motivo). Scope `notifications:read` |
| `GET /api/v1/in-app-notifications?userId=` | Lista o inbox **não lido** de um usuário, mais recentes primeiro. Scope `in-app-notifications:read` |
| `GET /api/v1/in-app-notifications/count?userId=` | Conta o inbox não lido — para o sino do header, sem a lista completa. Scope `in-app-notifications:read` |
| `POST /api/v1/in-app-notifications/{id}/read` | Marca um item como lido (idempotente). Scope `in-app-notifications:write` |

- **E-mail — assíncrono com retry** (ADR-0015 Camada 2): o SDK ganhou `IBackgroundJobScheduler`/`AddSeccoBackgroundJobs()` — Hangfire com storage SQL Server no **banco de plataforma** (nunca por tenant, seção `NotificationHub:BackgroundJobs`), `TenantJobRunner` restaura o tenant no escopo automaticamente antes do job rodar. `SendEmailJob` usa MailKit (SMTP) — `System.Net.Mail.SmtpClient` está obsoleto.
- **Falha e retry**: uma tentativa que falha marca `Failed` com o motivo (truncado) e relança a exceção — quem decide o retry é o Hangfire (`[AutomaticRetry(Attempts = 5)]`, fixado no runner do SDK). Uma tentativa seguinte bem-sucedida sobrescreve o status para `Sent`. **Simplificação consciente do v1**: enquanto há retry pendente, o status pode aparecer `Failed` transitoriamente em vez de "ainda tentando" — aceitável para o escopo atual, candidato a revisão se virar um problema real de UX.
- **In-app — síncrono**: a "entrega" é a própria escrita no banco do tenant, sem fila — não há Hangfire nem status de falha de entrega, só lido/não lido. Ciclo de vida deliberadamente separado do e-mail (entidade `InAppNotification` própria) — os dois nunca tiveram o mesmo formato de "sucesso/falha".
- **Conteúdo persistido**: e-mail grava destinatário/assunto/corpo; in-app grava título/mensagem/link (decisão consciente — o chamador já decidiu enviar esse conteúdo; sem isso, uma falha de e-mail não teria contexto para diagnóstico). O `GET` de e-mail não devolve assunto/corpo — só status.
- **Endereçamento**: `userId` (dono do item in-app) e `recipient` (e-mail) são campos independentes no mesmo request — o Hub nunca resolve um a partir do outro. Enviar para os dois canais numa chamada exige os dois.

## Configuração

| Seção | Uso |
|---|---|
| `NotificationHub:BackgroundJobs:ConnectionString` | Banco de PLATAFORMA do Hangfire (schema próprio, gerenciado pela lib) — nunca o banco de um tenant |
| `NotificationHub:Email` | `Host`/`Port`/`UseStartTls`/`Username`/`Password`/`FromAddress`/`FromName` do provider SMTP |
| `NotificationHub:Limits` | `MaxRecipientLength`/`MaxTitleLength`/`MaxMessageLength`/`MaxSourceLength`/`MaxTypeLength`/`MaxLinkLength` (ADR-0020) |
| `NotificationHub:Database` | Engine dos bancos de tenant (`SqlServer` padrão / `Postgres`, ADR-0018) |

### Paridade de banco (8.3, ADR-0018)

PostgreSQL como segundo provider **do banco de tenant** — migrations do assembly próprio (`Secco.NotificationHub.Migrations.Postgres`), schema idêntico (minúsculo, sem aspas), cobrindo `tb_notifications` e `tb_in_app_notifications`. O Hangfire (banco de **plataforma**) é **SQL-Server-only no v1**: é infraestrutura de fila, não dado de tenant, e não é alvo da paridade de provider da ADR-0018 — mudar isso exigiria o pacote `Hangfire.PostgreSql`, fora do escopo atual.

### Ambiente de desenvolvimento

`docker-compose.yml`: SQL Server (bancos de tenant + plataforma do Hangfire, criado por um serviço `sqlserver-init` — diferente do EF, o Hangfire não cria o banco sozinho) + MailHog (SMTP fake local, `localhost:8025` para ver os e-mails "enviados").

## Testes

`tests/NotificationHub/Secco.NotificationHub.Tests` (ADR-0012): Testcontainers reais (SQL Server) para os bancos de tenant **e** para o banco de plataforma do Hangfire — o job de envio é processado de verdade pelo Hangfire; só o SMTP é substituído por um fake (sem servidor de e-mail real em teste). Cobre: despacho para 1, 2 e nenhum canal, envio de e-mail bem-sucedido (`Pending` → `Sent`), falha marcada com motivo, listagem/contagem/leitura do inbox in-app, isolamento entre tenants (e-mail e in-app), validação de entrada, autorização por permissão, paridade PostgreSQL (migrations + repositório das duas entidades, sem WebApplicationFactory — o Hangfire não entra nessa suíte). `TenantJobRunner` também tem teste isolado no SDK (`Secco.SDK.AspNetCore.Tests`).

**Achado de teste**: múltiplas instâncias de `WebApplicationFactory` com Hangfire no mesmo processo de teste quebram — `Hangfire.Logging.LogProvider` é um bridge de log **estático por processo**; quando a primeira factory é descartada, qualquer uso posterior do Hangfire (por outra factory) lança `ObjectDisposedException` no `LoggerFactory` capturado. Resolvido com uma `ICollectionFixture` compartilhada entre as classes de teste do produto (uma única factory para todo o assembly).

## Backlog do produto

- 3º canal (push/SMS/webhook) — só quando houver demanda real de mais um canal.
- Motor de templates — só se 2+ produtos repetirem a mesma necessidade (critério do SharedKernel aplicado ao produto).
