# Secco.SDK.EntityFrameworkCore

Integração EF Core da Secco Platform: `SeccoDbContext` + `SeccoNamingConvention` — a notação húngara de banco (ADR-0017) aplicada por convention global. **Ninguém digita nome de tabela, coluna, constraint ou índice.**

## Uso

```csharp
public sealed class LogStreamDbContext(DbContextOptions<LogStreamDbContext> options)
    : SeccoDbContext(options)
{
    public DbSet<LogEntry> LogEntries { get; set; }   // → tb_log_entries
}
```

## Regras aplicadas

- **Tabela**: `tb_` + snake_case do nome do `DbSet` (o plural é o que o dev já escreve na propriedade; plural irregular = renomear a propriedade). `ToTable` explícito vence.
- **PK**: `id_pk_<classe>` (`LogEntry.Id` → `id_pk_log_entry`); **FK**: `id_fk_<referenciada>` (`TenantId` → `id_fk_tenant`; `CreatedByUserId` → `id_fk_created_by_user`); membro de PK composta que é FK: `id_pfk_*`.
- **Colunas por tipo CLR**: `bool` → `fl_` (sem `Is`/`Has`), enum → `ie_`, `string`/`char` → `ds_`, datas → `dt_`, `decimal` → `vl_` (quantidade? override com `[Column("qt_...")]`), numéricos → `nr_`.
- **Constraints e índices**: `pk_<tabela>`, `fk_<tabela>_<referenciada>`, `uk_<tabela>_<colunas>` (chaves alternadas e índices únicos), `idx_<tabela>_<colunas>` — migrations já nascem conformes.
- **`BaseEntity.DomainEvents`** é ignorado automaticamente (eventos de domínio nunca são persistidos).

Configuração explícita sempre vence a convention — é o mecanismo de exceção previsto na ADR-0017.

Agnóstico de provider (ADR-0018): este pacote referencia só o EF Core relacional base; `Microsoft.EntityFrameworkCore.SqlServer` (padrão) ou `Npgsql.EntityFrameworkCore.PostgreSQL` são referenciados pelo produto.

## Seeding (ADR-0019)

Produtos implementam seus seeders em `Infrastructure/Seeding/` e os registram no DI; o disparo é sempre **explícito** (`Program.cs` em DEV, provisionamento de tenant, job pós-migration — nunca efeito automático de startup em produção):

```csharp
builder.Services.AddScoped<IReferenceDataSeeder, LogStreamReferenceDataSeeder>();
builder.Services.AddScoped<IDevelopmentDataSeeder, LogStreamDevelopmentDataSeeder>();
// ...
await app.Services.SeedSeccoDataAsync();
```

- **`IReferenceDataSeeder`** — roda em todos os ambientes; implementação obrigatoriamente idempotente (upsert por chave natural, IDs determinísticos).
- **`IDevelopmentDataSeeder`** — roda somente sob a **guarda dupla**: `IsDevelopment()` **e** `Secco:Seed:Development = true` (fail-closed: sem `IHostEnvironment` registrado, não roda; em DEV sem a flag, o skip é logado). Executa sempre após os de referência. Dados via Bogus `pt_BR` com seed fixo — referenciado pela Infrastructure do produto, não por este pacote.
- `Order` (default 0) ordena seeders do mesmo tipo.

## Cifragem de segredo em repouso (ADR-0025, promovido na ADR-0029)

`ISeccoSecretCipher` + `AesGcmSecretCipher` cifram um segredo guardado em coluna, no formato versionado `secco-enc:v1:<base64(nonce ‖ ciphertext ‖ tag)>`. AES-256-GCM é AEAD: o tag autentica o dado, então adulteração falha em vez de decifrar lixo. Sem dependência externa — `AesGcm` é BCL.

```csharp
var cipher = AesGcmSecretCipher.FromBase64Keys(activeKey, retiredKeys);
```

O cifrador recebe as chaves **já decodificadas** e não conhece configuração de produto nenhum. É deliberado: a política de onde a chave vem — chave embutida em DEV, fail-fast em Production — é de cada produto, e impô-la aqui valeria para todo consumidor.

Valor **sem** o prefixo é tratado como legado em claro e devolvido como está; é o que permite converger uma base existente sem migração manual. `IsEncryptedWithActiveKey` é a condição de convergência da rotação: cifrado com chave aposentada devolve `false` e é re-cifrado.

Nasceu dentro do `Secco.SecureGate` e subiu para cá quando o segundo produto precisou do mesmo formato — o adotante já havia reimplementado o `secco-enc:` por conta própria, e o terceiro consumidor faria três implementações independentes do mesmo formato criptográfico divergindo entre si.
