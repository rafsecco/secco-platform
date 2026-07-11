using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Secco.SharedKernel.Entities;
using static Secco.SDK.EntityFrameworkCore.Conventions.SnakeCaseNamer;

namespace Secco.SDK.EntityFrameworkCore.Conventions;

/// <summary>
/// Convention global da notação húngara de banco da plataforma (ADR-0017): ninguém digita
/// nome de tabela, coluna, constraint ou índice — a tradução C# → banco acontece aqui.
/// Configuração explícita (<c>[Column]</c>, <c>ToTable</c>, <c>HasName</c>...) sempre vence:
/// é o mecanismo de override para exceções (ex.: <c>decimal</c> que é quantidade → <c>qt_</c>).
/// Registrada automaticamente pelo <see cref="SeccoDbContext"/>.
/// </summary>
public sealed class SeccoNamingConvention : IModelFinalizingConvention
{
    /// <summary>Aplica a nomenclatura a todo o modelo ao final da sua construção.</summary>
    /// <param name="modelBuilder">Builder do modelo em finalização.</param>
    /// <param name="context">Contexto da execução de conventions.</param>
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var entityTypes = modelBuilder.Metadata.GetEntityTypes().ToList();

        // Fases separadas: tabelas antes de colunas (nomes de constraint usam a tabela),
        // colunas antes de constraints/índices (uk_/idx_ usam os nomes finais das colunas).
        foreach (var entityType in entityTypes)
        {
            IgnoreDomainEvents(entityType);
            RenameTable(entityType);
        }

        foreach (var entityType in entityTypes)
        {
            RenameColumns(entityType);
        }

        foreach (var entityType in entityTypes)
        {
            RenameKeysForeignKeysAndIndexes(entityType);
        }
    }

    /// <summary>
    /// <see cref="BaseEntity.DomainEvents"/> tem backing field que o EF mapearia como
    /// navegação — eventos de domínio nunca são persistidos.
    /// </summary>
    private static void IgnoreDomainEvents(IConventionEntityType entityType)
    {
        if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
        {
            entityType.Builder.Ignore(nameof(BaseEntity.DomainEvents));
        }
    }

    private static void RenameTable(IConventionEntityType entityType)
    {
        // TPH: filhos compartilham a tabela do pai; owned types vivem na tabela do dono.
        if (entityType.BaseType is not null || entityType.IsOwned())
        {
            return;
        }

        var tableName = entityType.GetTableName();

        if (tableName is null || tableName.StartsWith("tb_", StringComparison.Ordinal))
        {
            return;
        }

        // Plural vem do nome do DbSet (default do EF) — decisão de design registrada no README.
        entityType.Builder.ToTable("tb_" + ToSnakeCase(tableName));
    }

    private static void RenameColumns(IConventionEntityType entityType)
    {
        foreach (var property in entityType.GetDeclaredProperties())
        {
            // Builder com source Convention: [Column]/HasColumnName explícitos vencem.
            property.Builder.HasColumnName(BuildColumnName(entityType, property));
        }
    }

    private static string BuildColumnName(IConventionEntityType entityType, IConventionProperty property)
    {
        var isPrimaryKey = property.IsPrimaryKey();
        var isForeignKey = property.IsForeignKey();

        if (isPrimaryKey && isForeignKey)
        {
            return BuildKeyColumnName(property, "id_pfk_");
        }

        if (isPrimaryKey)
        {
            var primaryKey = entityType.FindPrimaryKey()!;

            // PK simples leva o nome da entidade; membro não-FK de PK composta usa o
            // próprio nome (a ADR-0017 só define o caso simples e o associativo).
            return primaryKey.Properties.Count == 1
                ? "id_pk_" + ToSnakeCase(entityType.ShortName())
                : "id_pk_" + ToSnakeCase(StripIdSuffix(property.Name));
        }

        if (isForeignKey)
        {
            return BuildKeyColumnName(property, "id_fk_");
        }

        return BuildTypePrefix(property) + ToSnakeCase(StripBooleanPrefix(property.Name));
    }

    /// <summary>
    /// FK nomeada pela entidade referenciada (<c>TenantId</c> → <c>id_fk_tenant</c>); quando a
    /// propriedade diz mais que o alvo (<c>CreatedByUserId</c> → <c>id_fk_created_by_user</c>),
    /// o nome dela prevalece — evita colisão entre duas FKs para a mesma entidade.
    /// </summary>
    private static string BuildKeyColumnName(IConventionProperty property, string prefix)
    {
        var principalName = property.GetContainingForeignKeys().First().PrincipalEntityType.ShortName();
        var propertyBaseName = StripIdSuffix(property.Name);

        var chosen = propertyBaseName.Equals(principalName, StringComparison.OrdinalIgnoreCase)
            ? principalName
            : propertyBaseName;

        return prefix + ToSnakeCase(chosen);
    }

    private static string BuildTypePrefix(IConventionProperty property)
    {
        var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (clrType == typeof(bool))
        {
            return "fl_";
        }

        if (clrType.IsEnum)
        {
            return "ie_";
        }

        if (clrType == typeof(string) || clrType == typeof(char))
        {
            return "ds_";
        }

        if (clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset)
            || clrType == typeof(DateOnly) || clrType == typeof(TimeOnly))
        {
            return "dt_";
        }

        // decimal → vl_ por padrão; quantidade/métrica usa override explícito ([Column]) — ADR-0017.
        if (clrType == typeof(decimal))
        {
            return "vl_";
        }

        if (clrType == typeof(int) || clrType == typeof(long) || clrType == typeof(short)
            || clrType == typeof(byte) || clrType == typeof(sbyte)
            || clrType == typeof(uint) || clrType == typeof(ulong) || clrType == typeof(ushort)
            || clrType == typeof(float) || clrType == typeof(double))
        {
            return "nr_";
        }

        // Guid não-chave, byte[], tipos complexos: sem prefixo definido na ADR-0017.
        return string.Empty;
    }

    private static void RenameKeysForeignKeysAndIndexes(IConventionEntityType entityType)
    {
        var tableName = entityType.GetTableName();

        if (tableName is null)
        {
            return;
        }

        var bareTable = tableName.StartsWith("tb_", StringComparison.Ordinal) ? tableName[3..] : tableName;

        foreach (var key in entityType.GetDeclaredKeys())
        {
            key.Builder.HasName(key.IsPrimaryKey()
                ? "pk_" + bareTable
                : "uk_" + bareTable + "_" + JoinColumnNames(key.Properties));
        }

        var usedForeignKeyNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
        {
            var constraintName = "fk_" + bareTable + "_" + ToSnakeCase(foreignKey.PrincipalEntityType.ShortName());

            // Duas FKs para a mesma entidade: desambigua com as colunas.
            if (!usedForeignKeyNames.Add(constraintName))
            {
                constraintName += "_" + JoinColumnNames(foreignKey.Properties);
                usedForeignKeyNames.Add(constraintName);
            }

            foreignKey.Builder.HasConstraintName(constraintName);
        }

        foreach (var index in entityType.GetDeclaredIndexes())
        {
            var prefix = index.IsUnique ? "uk_" : "idx_";
            index.Builder.HasDatabaseName(prefix + bareTable + "_" + JoinColumnNames(index.Properties));
        }
    }

    private static string JoinColumnNames(IReadOnlyList<IConventionProperty> properties) =>
        string.Join('_', properties.Select(property => property.GetColumnName()));
}
