---
name: secco-db-naming
description: Notação húngara de banco de dados da Secco Platform (ADR-0017). Usar SEMPRE que a tarefa envolver criação ou alteração de schema de banco, entidades EF Core, configurações de mapeamento (IEntityTypeConfiguration), migrations, scripts SQL, views, índices, constraints, procedures ou functions — em qualquer projeto Secco.*, RS.* legado, ou quando o usuário mencionar prefixos como tb_, id_, ds_, dt_, fl_, notação húngara de banco, ou nomenclatura de tabelas/colunas.
---

# Secco Platform — Nomenclatura de Banco de Dados (ADR-0017)

Todo objeto de banco segue notação húngara com prefixos **minúsculos** — único formato com comportamento idêntico nos engines suportados (SQL Server preserva a caixa; PostgreSQL dobra não-citados para minúsculas). Jamais usar identificadores que exijam aspas/colchetes. **Provider padrão: SQL Server; segundo provider: PostgreSQL (ADR-0018)** — SQL cru é escrito por provider, com a mesma nomenclatura.

## Prefixos de coluna

Formato: `<prefixo><snake_case_do_nome>`

| Prefixo | Semântica | Tipo CLR típico | Exemplo |
|---|---|---|---|
| `id_pk_` | chave primária | Guid, int, long (chave) | `id_pk_log_entry` |
| `id_fk_` | chave estrangeira | (chave) | `id_fk_tenant` |
| `id_pfk_` | membro de PK composta que também é FK | (chave, associativas) | `id_pfk_user` |
| `ds_` | texto/descrição | string | `ds_message`, `ds_email` |
| `dt_` | data/hora | DateTime, DateTimeOffset, DateOnly | `dt_created_at` |
| `nr_` | número/métrica | int, long, double | `nr_attempts`, `nr_duration_ms` |
| `ie_` | enum/indicador | enum | `ie_log_level`, `ie_status` |
| `fl_` | flag/booleano | bool | `fl_active` |
| `vl_` | valor monetário | decimal | `vl_price`, `vl_total` |
| `qt_` | quantidade | int/decimal (contagem) | `qt_items` |

Regras:
- PK: `id_pk_<entidade no singular>` (`tb_log_entries` → PK `id_pk_log_entry`).
- FK: `id_fk_<tabela referenciada no singular>` (`id_fk_tenant`).
- Membro de PK composta que também é FK (tabelas associativas): `id_pfk_<referenciada no singular>` (`tb_user_roles` → `id_pfk_user`, `id_pfk_role`).
- A mesma coluna lógica tem nome distinto em cada lado do relacionamento — intencional: `ON le.id_fk_tenant = t.id_pk_tenant` explicita a direção do JOIN.
- Booleanos descartam `Is`/`Has` do C#: `IsActive` → `fl_active`, `HasErrors` → `fl_errors`.
- `decimal` → `vl_` por padrão; se for quantidade ou métrica, override explícito para `qt_`/`nr_`.

## Prefixos de objeto

| Prefixo | Objeto | Padrão de nome | Exemplo |
|---|---|---|---|
| `tb_` | tabela | `tb_<plural>` | `tb_log_entries` |
| `vw_` | view | `vw_<descrição>` | `vw_active_tenants` |
| `pk_` | primary key constraint | `pk_<tabela sem tb_>` | `pk_log_entries` |
| `fk_` | foreign key constraint | `fk_<tabela>_<referenciada>` | `fk_log_entries_tenant` |
| `uk_` | unique constraint | `uk_<tabela>_<colunas>` | `uk_tenants_ds_slug` |
| `idx_` | índice | `idx_<tabela>_<colunas>` | `idx_log_entries_dt_created_at` |
| `ft_` | índice full-text | `ft_<tabela>_<colunas>` | `ft_log_entries_ds_message` |
| `sp_` | procedure (mutação/batch) | `sp_<verbo>_<nome>` | `sp_purge_old_logs` |
| `fn_` | function (retorna dados) | `fn_<verbo>_<nome>` | `fn_select_active_tenants` |

### Verbos de procedures/functions

Vocabulário CRUD padronizado: `select` (listagem), `get` (leitura pontual), `insert`, `update`, `delete`, `upsert`. Operações de negócio usam verbo descritivo livre: `sp_purge_old_logs`, `sp_rebuild_tenant_stats`, `fn_calculate_retention`. Se não dá para nomear o verbo, a rotina faz coisa demais — dividir.

Convenção semântica válida em qualquer engine: `fn_*` **retorna dados**; `sp_*` fica para mutações e batches (no PostgreSQL isso coincide com a natureza de functions vs procedures; no SQL Server é disciplina da plataforma).

Nota SQL Server: o prefixo `sp_` foi **mantido por decisão registrada na ADR-0018** — o custo de lookup no banco `master` é conhecido e aceito. Usar `sp_` normalmente; não propor `usp_`.

## Regra de ouro: ninguém digita nomes de coluna

A tradução C# → banco é responsabilidade de uma **convention global do EF Core** registrada no `DbContext` base da plataforma. Código de entidade e configurações permanecem limpos; `[Column("...")]` explícito só para exceções (ex.: decimal que é quantidade).

Implementação de referência da convention:

```csharp
public sealed class SeccoNamingConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        {
            entity.Builder.ToTable($"tb_{ToSnakeCase(Pluralize(entity.DisplayName()))}");

            foreach (var property in entity.GetDeclaredProperties())
            {
                // Respeita [Column] explícito
                if (property.GetColumnName() != property.Name &&
                    property.GetAnnotation(RelationalAnnotationNames.ColumnName) is not null)
                    continue;

                property.Builder.HasColumnName(BuildColumnName(entity, property));
            }
        }
    }

    private static string BuildColumnName(IConventionEntityType entity, IConventionProperty p)
    {
        var name = ToSnakeCase(StripBooleanPrefix(p.Name));
        var clr = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;

        var isPk = p.IsPrimaryKey();
        var isFk = p.IsForeignKey();

        if (isPk && isFk)   // PK composta + FK (tabela associativa)
            return $"id_pfk_{ToSnakeCase(Singularize(TargetEntityName(p)))}";
        if (isPk)
            return $"id_pk_{ToSnakeCase(Singularize(entity.DisplayName()))}";
        if (isFk)
            return $"id_fk_{ToSnakeCase(Singularize(TargetEntityName(p)))}";

        var prefix = clr switch
        {
            _ when clr == typeof(bool) => "fl_",
            _ when clr.IsEnum => "ie_",
            _ when clr == typeof(string) => "ds_",
            _ when clr == typeof(DateTime) || clr == typeof(DateTimeOffset)
                 || clr == typeof(DateOnly) => "dt_",
            _ when clr == typeof(decimal) => "vl_",   // override p/ qt_/nr_ via [Column]
            _ when clr.IsPrimitive => "nr_",
            _ => ""
        };
        return prefix + name;
    }
    // ToSnakeCase / Pluralize / Singularize / StripBooleanPrefix: helpers do SharedKernel
}
```

Registro no `DbContext` base:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    => builder.Conventions.Add(_ => new SeccoNamingConvention());
```

## Checklist ao tocar em schema

- [ ] Todos os identificadores minúsculos, sem necessidade de aspas.
- [ ] Colunas com prefixo semântico correto (atenção a `vl_` vs `qt_` vs `nr_` em decimais).
- [ ] Chaves no padrão `id_pk_` / `id_fk_` / `id_pfk_` (associativas usam `id_pfk_`).
- [ ] Constraints e índices nomeados explicitamente na migration (`pk_`, `fk_`, `uk_`, `idx_`, `ft_`) — não aceitar os nomes gerados pelo EF.
- [ ] Nenhum `HasColumnName`/`[Column]` manual que a convention já cobriria.
- [ ] SQL cru (views, functions, procedures) segue os mesmos prefixos; rotinas com verbo padronizado (`sp_`/`fn_` + `select|get|insert|update|delete|upsert` ou verbo de negócio).
