# Migrations e seed de referência fora de Development — design

**Data:** 2026-10-08
**Status:** aprovada (2026-10-08)
**Decisões arquiteturais:** [ADR-0038](../../adr/secco-platform-adrs.md) (esta entrega), [ADR-0005](../../adr/secco-platform-adrs.md) (processo controlado), [ADR-0019](../../adr/secco-platform-adrs.md) (seed de referência), [ADR-0028](../../adr/secco-platform-adrs.md) (provisionamento de banco de tenant), [ADR-0037](../../adr/secco-platform-adrs.md) (clients de plataforma no seed), [ADR-0003](../../adr/secco-platform-adrs.md) (admissão no SharedKernel), [ADR-0020](../../adr/secco-platform-adrs.md) (segurança)
**Origem:** issue [#34](https://github.com/rafsecco/secco-platform/issues/34), achada no design da #31.

## Contexto

Confirmado no código em 2026-10-08:

- `src/SecureGate/Secco.SecureGate.Api/Program.cs`, `src/LogStream/Secco.LogStream.Api/Program.cs`, `src/NotificationHub/Secco.NotificationHub.Api/Program.cs` e `templates/secco-service/Secco.SampleService.Api/Program.cs` só chamam migrations e `SeedSeccoDataAsync()` dentro de `if (app.Environment.IsDevelopment())`.
- Rotinas de migração existentes: `MigrateSecureGateDatabaseAsync` (banco de plataforma), `MigrateLogStreamTenantDatabasesAsync` e `MigrateNotificationHubTenantDatabasesAsync` (todos os tenants do `ITenantCatalog`).
- Só o SecureGate tem seeders de referência; LogStream e NotificationHub não têm nenhum.
- `Secco.SDK.AspNetCore` e `Secco.SDK.EntityFrameworkCore` não se referenciam; ambos referenciam o `Secco.SharedKernel`.
- `SeccoTenancyExceptionMiddleware` (SDK AspNetCore) traduz `TenantCatalogUnavailableException` em `503` com `Retry-After: 15`.
- O EF Core 10 adquire lock exclusivo em `MigrateAsync` (visto ao aplicar migrations do SecureGate num PostgreSQL descartável).
- O job `docker-stacks` do CI sobe cada profile do `docker-compose.yml` e faz smoke em `/health`; os serviços de API rodam em `Development`.

## Decisões (aprovadas na conversa de design)

1. **Mecanismo:** verbo `migrate` no próprio binário, executado pelo deploy antes das réplicas.
2. **Tenant criado depois do deploy:** migração no primeiro uso, por interceptor de conexão.
3. **DEV e Compose:** DEV continua automático pelo mesmo código; Compose ganha um serviço `*-migrate` por produto, e a API depende dele.

## SharedKernel — `SeccoTransientException`

`src/SharedKernel/Secco.SharedKernel/Exceptions/SeccoTransientException.cs`:

```csharp
/// Falha transitória de infraestrutura: o chamador pode tentar de novo (ADR-0038).
public abstract class SeccoTransientException : SeccoException { /* três construtores protegidos, como SeccoException */ }
```

- `TenantCatalogUnavailableException` passa a derivar dela (mesma mensagem, mesmo comportamento).
- `SeccoTenancyExceptionMiddleware` captura `SeccoTransientException` (em vez do tipo concreto) e responde `503` + `Retry-After: 15`, ProblemDetails com a mensagem da exceção — que nunca carrega connection string.
- Versão: minor no SharedKernel (tipo novo, aditivo).

## SDK EF Core — porta, rotina e comando

Em `src/SDK/Secco.SDK.EntityFrameworkCore/Migrations/`:

```csharp
public interface ISeccoDatabaseMigrator
{
    /// Nome para log (ex.: "SecureGate (plataforma)", "LogStream (tenants)").
    string Name { get; }

    /// Aplica as migrations. Devolve os alvos que falharam (vazio = sucesso total).
    /// Um alvo que falha não interrompe os demais.
    Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default);
}

public static class SeccoMigrationExtensions
{
    /// Roda todos os ISeccoDatabaseMigrator registrados; se nenhum falhar, roda
    /// SeedSeccoDataAsync. Devolve true em sucesso total. Loga cada falha (nome do
    /// migrator + alvo), nunca connection string.
    public static Task<bool> RunSeccoMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default);
}

public static class SeccoCommands
{
    /// true quando o primeiro argumento é exatamente "migrate" (ordinal).
    public static bool IsMigrate(string[] args);
}
```

- O alvo devolvido em falha é o id do tenant (`TenantInfo.TenantId`) ou o nome do banco de plataforma — nunca a connection string.
- `RunSeccoMigrationsAsync` não chama o seed se qualquer migrator reportar falha ou lançar exceção (a exceção é logada e conta como falha).

## SDK EF Core — migração no primeiro uso

Em `src/SDK/Secco.SDK.EntityFrameworkCore/Migrations/`:

- `SeccoTenantMigrationGate` (singleton): `ConcurrentDictionary<string, Lazy<Task>>` com chave = SHA-256 hex da connection string. `EnsureMigratedAsync(string connectionString, Func<string, DbContext> createContext, CancellationToken)`:
  - entrada existente → aguarda a mesma tarefa;
  - tarefa nova → cria o contexto com `createContext(connectionString)`, `GetPendingMigrationsAsync`, e `MigrateAsync` só se houver pendência;
  - falha → remove a entrada e lança `TenantDatabaseUnavailableException(inner)`.
- `TenantDatabaseUnavailableException : SeccoTransientException`, mensagem fixa "O banco de dados do tenant está temporariamente indisponível." (sem connection string, sem id).
- `SeccoTenantMigrationInterceptor<TContext> : DbConnectionInterceptor` (genérico para cada produto ter a sua fábrica) — em `ConnectionOpeningAsync` e `ConnectionOpening` (síncrono, via `GetAwaiter().GetResult()` no gate) chama o gate com `connection.ConnectionString` e a fábrica do produto. O contexto de migração é criado **sem** o interceptor.
- Registro: `services.AddSeccoTenantMigrations<TContext>(Func<string, TContext> createMigrationContext)` registra o gate (singleton, `TryAdd`) e o interceptor para o contexto; o produto acrescenta `options.AddInterceptors(serviceProvider.GetRequiredService<SeccoTenantMigrationInterceptor<TContext>>())` no seu `AddDbContext`.
- Log da falha: `LoggerMessage` com o tenant do `ITenantContext`/ambiente quando disponível e a mensagem da exceção interna; nunca a connection string.

## Produtos

**SecureGate**
- `SecureGatePlatformMigrator : ISeccoDatabaseMigrator` envolve `MigrateSecureGateDatabaseAsync`.
- `Program.cs`: verbo `migrate` → `return await app.Services.RunSeccoMigrationsAsync() ? 0 : 1;`; em Development, `await app.Services.RunSeccoMigrationsAsync();`; fora de Development, antes de `RunAsync`, checa `GetPendingMigrationsAsync` no banco de plataforma e, se houver pendência, loga e encerra com código 1 ("rode `dotnet Secco.SecureGate.Api.dll migrate`").
- Sem interceptor (banco de plataforma).

**LogStream e NotificationHub**
- `*TenantMigrator : ISeccoDatabaseMigrator` envolve a rotina por tenant atual, agora coletando falhas por tenant em vez de parar na primeira.
- `AddDbContext` do contexto de tenant ganha o interceptor; `AddSeccoTenantMigrations<TContext>` com a fábrica que já existe (`*DatabaseProviderConfigurator.CreateOptions(provider, connectionString)`).
- `Program.cs`: mesmo padrão do SecureGate, **sem** a checagem de pendência no startup.
- O banco de plataforma do Hangfire (NotificationHub) segue como hoje: o próprio Hangfire prepara o schema.

**Template `secco-service`**: mesmo padrão dos produtos com tenant (migrator, interceptor, `Program.cs`). O job `validate-template` do CI cobre.

## Docker Compose

Para cada produto:

```yaml
  securegate-migrate:
    profiles: ["securegate", "all"]
    build: { ...igual ao da API... }
    command: ["migrate"]          # vira argumento do ENTRYPOINT ["dotnet", "Secco.SecureGate.Api.dll"]
    environment: { ...igual ao da API... }
    depends_on: { ...igual ao da API... }
    restart: "no"
  securegate-api:
    depends_on:
      securegate-migrate:
        condition: service_completed_successfully
```

Para não duplicar `build`/`environment`, usar uma âncora YAML (`x-securegate: &securegate`) por produto.

## Segurança (ADR-0020)

| Ameaça | Barreira |
| --- | --- |
| Connection string em log ou resposta | alvos de falha são ids; gate guarda hash; mensagens de exceção fixas |
| Input externo governando DDL | migrations vêm do assembly do produto; tenant vem do catálogo, nunca do chamador |
| Privilégio novo no runtime | nenhum: credencial do tenant já tem `db_owner` (ADR-0028); o banco de plataforma não migra no runtime |
| Seed destrutivo concorrente no boot | seed só no `migrate` e em DEV; SecureGate recusa subir com migration pendente |
| DoS por tenant problemático | falha isolada por tenant (`503` só nele); comando continua os outros |
| Falha memorizada bloqueando tenant | entrada removida em falha; próxima abertura tenta de novo |

## Testes

SharedKernel (unit): `TenantCatalogUnavailableException` é `SeccoTransientException`.

SDK EF Core (unit):
- `IsMigrate`: `["migrate"]` → true; `[]`, `["Migrate"]`, `["--migrate"]`, `["x","migrate"]` → false.
- `RunSeccoMigrationsAsync`: roda todos os migrators e depois o seed; não roda o seed se um migrator devolver falha; não roda o seed se um migrator lançar; devolve false nos dois casos.
- Gate: duas chamadas concorrentes para o mesmo banco executam a fábrica uma vez; falha remove a entrada e a chamada seguinte executa de novo; falha vira `TenantDatabaseUnavailableException` com a original como `InnerException`; a chave guardada não contém a connection string.

SDK EF Core (integração, Testcontainers): contexto de teste com o interceptor sobre um banco vazio — a primeira consulta cria o schema; a segunda abertura não consulta pendências de novo.

SDK AspNetCore: o middleware traduz qualquer `SeccoTransientException` em `503` + `Retry-After`.

LogStream (integração): tenant novo no catálogo, banco vazio, sem reiniciar a API → `POST /api/v1/log-entries` com token do tenant é aceito; a tabela de histórico do banco tem as migrations.

NotificationHub (integração): o mesmo, com `POST /api/v1/notifications` em canal `in_app`.

SecureGate (integração): host fora de Development com banco sem migrations → startup falha com a mensagem do `migrate`; `RunSeccoMigrationsAsync` com `PlatformClients` configurado → sucesso, migrations aplicadas e clients reconciliados.

Mutação (prova obrigatória, como nas entregas anteriores):
1. seed roda mesmo com migrator falho → teste do `RunSeccoMigrationsAsync` falha;
2. gate não remove a entrada em falha → teste de nova tentativa falha;
3. interceptor ausente no contexto de tenant do LogStream → teste do tenant novo falha;
4. checagem de pendência removida do SecureGate → teste de startup falha;
5. middleware captura só o tipo concreto antigo → teste do `503` do banco falha.

## Documentação

- `docs/getting-started.md`: seção "Implantar" — `migrate` antes das réplicas; exemplos de passo de pipeline e de *init container*; o que acontece com tenant novo; o SecureGate recusa subir com migration pendente.
- `docs/testing-guide.md`: o serviço `*-migrate` do compose.
- `CHANGELOG.md`: SharedKernel (tipo novo), SDK AspNetCore (middleware genérico), SDK EF Core (porta, rotina, comando, interceptor), template.
- `docs/roadmap.md` (#34 entregue; dependência da #31 resolvida), `CLAUDE.md` (estado), `docs/design-decisions-log.md`.
- Comentário na #31 apontando que a dependência foi resolvida.

## Publicação

Minor em `Secco.SharedKernel`, `Secco.SDK.AspNetCore`, `Secco.SDK.EntityFrameworkCore` e `Secco.Templates`; patches de cadeia onde o script de release pedir. Seguir a skill `secco-platform-release`.

## Fatias sugeridas para o plano

1. SharedKernel + middleware genérico.
2. SDK EF Core: porta, rotina, comando (unit).
3. SDK EF Core: gate + interceptor (unit + integração).
4. SecureGate: migrator, `Program.cs`, checagem de pendência.
5. LogStream e NotificationHub: migrators, interceptor, `Program.cs`, testes de tenant novo.
6. Template + Compose + CI.
7. Mutação, documentação, verificação completa.

## Fora desta entrega

- `CREATE DATABASE` dos bancos de plataforma (SecureGate, Hangfire) — DBA.
- Seed de referência por tenant (ADR-0019 o prevê; nenhum produto tem).
- Expand/contract de schema com réplicas de versões diferentes no mesmo deploy.
- Separação formal de credencial de DDL × runtime (possível pela configuração do passo `migrate`, sem mecanismo dedicado).
