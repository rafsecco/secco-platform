using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests.Conventions;

public class SeccoNamingConventionTests
{
    private static readonly IModel Model = BuildModel();

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Encrypt=false")
            .Options;

        using var context = new TestDbContext(options);
        return context.Model;
    }

    private static IEntityType Entity<T>() => Model.FindEntityType(typeof(T))!;

    private static string ColumnOf<T>(string propertyName) =>
        Entity<T>().FindProperty(propertyName)!.GetColumnName()!;

    // ---- Tabelas ----

    [Theory]
    [InlineData(typeof(Tenant), "tb_tenants")]
    [InlineData(typeof(LogEntry), "tb_log_entries")]
    [InlineData(typeof(UserRole), "tb_user_roles")]
    public void TableName_FromDbSetName_GetsTbPrefixAndSnakeCase(Type entityType, string expected)
    {
        Model.FindEntityType(entityType)!.GetTableName().Should().Be(expected);
    }

    [Fact]
    public void TableName_WithExplicitToTable_IsNotOverridden()
    {
        Entity<Legacy>().GetTableName().Should().Be("legacy_custom");
    }

    // ---- Chaves ----

    [Fact]
    public void PrimaryKey_OnBaseEntityId_UsesEntitySingularName()
    {
        ColumnOf<Tenant>(nameof(Tenant.Id)).Should().Be("id_pk_tenant");
        ColumnOf<LogEntry>(nameof(LogEntry.Id)).Should().Be("id_pk_log_entry");
    }

    [Fact]
    public void ForeignKey_NamedAfterPrincipal_UsesIdFkPrefix()
    {
        ColumnOf<LogEntry>(nameof(LogEntry.TenantId)).Should().Be("id_fk_tenant");
    }

    [Fact]
    public void CompositeKeyMembers_ThatAreAlsoForeignKeys_UseIdPfkPrefix()
    {
        ColumnOf<UserRole>(nameof(UserRole.UserId)).Should().Be("id_pfk_user");
        ColumnOf<UserRole>(nameof(UserRole.RoleId)).Should().Be("id_pfk_role");
    }

    // ---- Prefixos por tipo ----

    [Theory]
    [InlineData(nameof(Tenant.Name), "ds_name")]
    [InlineData(nameof(Tenant.IsActive), "fl_active")]
    [InlineData(nameof(Tenant.Level), "ie_level")]
    [InlineData(nameof(Tenant.Balance), "vl_balance")]
    [InlineData(nameof(Tenant.Attempts), "nr_attempts")]
    [InlineData(nameof(Tenant.CreatedAt), "dt_created_at")]
    public void Columns_ByClrType_GetSemanticPrefix(string propertyName, string expected)
    {
        ColumnOf<Tenant>(propertyName).Should().Be(expected);
    }

    [Fact]
    public void Column_WithMultiWordName_KeepsSnakeCaseAfterPrefix()
    {
        ColumnOf<LogEntry>(nameof(LogEntry.DurationMs)).Should().Be("nr_duration_ms");
        ColumnOf<LogEntry>(nameof(LogEntry.OccurredAt)).Should().Be("dt_occurred_at");
    }

    [Fact]
    public void Column_WithExplicitColumnAttribute_IsNotOverridden()
    {
        ColumnOf<LogEntry>(nameof(LogEntry.Items)).Should().Be("qt_items");
    }

    // ---- Constraints e índices ----

    [Fact]
    public void PrimaryKeyConstraint_Always_UsesPkPrefixWithBareTableName()
    {
        Entity<Tenant>().FindPrimaryKey()!.GetName().Should().Be("pk_tenants");
        Entity<UserRole>().FindPrimaryKey()!.GetName().Should().Be("pk_user_roles");
    }

    [Fact]
    public void ForeignKeyConstraint_Always_UsesFkPrefixWithTableAndPrincipal()
    {
        Entity<LogEntry>().GetForeignKeys().Single()
            .GetConstraintName().Should().Be("fk_log_entries_tenant");
    }

    [Fact]
    public void UniqueIndex_Always_UsesUkPrefixWithFinalColumnNames()
    {
        Entity<Tenant>().GetIndexes().Single(i => i.IsUnique)
            .GetDatabaseName().Should().Be("uk_tenants_ds_slug");
    }

    [Fact]
    public void NonUniqueIndex_Always_UsesIdxPrefixWithFinalColumnNames()
    {
        Entity<LogEntry>().GetIndexes()
            .Single(i => i.Properties.Any(p => p.Name == nameof(LogEntry.OccurredAt)))
            .GetDatabaseName().Should().Be("idx_log_entries_dt_occurred_at");
    }

    [Fact]
    public void ForeignKeyAutoIndex_Always_UsesIdxPrefixWithFinalColumnNames()
    {
        Entity<LogEntry>().GetIndexes()
            .Single(i => i.Properties.Any(p => p.Name == nameof(LogEntry.TenantId)))
            .GetDatabaseName().Should().Be("idx_log_entries_id_fk_tenant");
    }

    // ---- BaseEntity ----

    [Fact]
    public void DomainEvents_OnBaseEntityDescendants_IsNotMapped()
    {
        Entity<Tenant>().FindProperty("DomainEvents").Should().BeNull();
        Entity<Tenant>().GetNavigations().Should().NotContain(n => n.Name == "DomainEvents");
    }
}
