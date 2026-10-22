using Microsoft.EntityFrameworkCore.Migrations;

namespace Secco.SecureGate.Migrations.Postgres;

/// <summary>
/// SQL do Row-Level Security do banco de plataforma do SecureGate em PostgreSQL (ADR-0041, camada 2):
/// <c>ENABLE</c> + <c>FORCE ROW LEVEL SECURITY</c> e uma politica <c>pol_tenant_isolation_&lt;tabela&gt;</c>
/// por tabela, com <c>USING</c> e <c>WITH CHECK</c>. Migrations futuras que alterem tabelas protegidas
/// chamam <see cref="Drop"/> no comeco e <see cref="Create"/> no fim, por simetria com o SQL Server
/// (aqui nao ha <c>SCHEMABINDING</c>, mas alterar colunas usadas nos predicados exige recriar a politica).
/// </summary>
public static class TenantRowLevelSecurity
{
    private const string Mode = "current_setting('secco.mode', true)";

    private const string Tenant = "nullif(current_setting('secco.tenant', true), '')::uuid";

    // Tabelas com tenant direto: (tabela, coluna do tenant)
    private static readonly (string Table, string Column)[] DirectTenantTables =
    [
        ("tb_roles", "id_fk_tenant"),
        ("tb_tenant_memberships", "id_pfk_tenant"),
        ("tb_tenant_databases", "id_fk_tenant"),
        ("tb_tenant_federations", "id_fk_tenant"),
        ("tb_tenant_group_role_mappings", "id_fk_tenant"),
        ("tb_elevation_grants", "id_fk_tenant"),
    ];

    // Revisao 2 (ADR-0044): tabelas com tenant direto acrescentadas depois da ADR-0041
    private static readonly (string Table, string Column)[] DirectTenantTablesAdr0044 =
    [
        ("tb_tenant_admin_grants", "id_fk_tenant"),
    ];

    // Revisao 3 (ADR-0054): tabelas com tenant direto acrescentadas depois da ADR-0044
    private static readonly (string Table, string Column)[] DirectTenantTablesAdr0054 =
    [
        ("tb_authority_audit_entries", "id_fk_tenant"),
    ];

    // Atribuição e permissão: seguem o tenant do perfil referenciado
    private static readonly (string Table, string Column)[] RoleBoundTables =
    [
        ("tb_role_claims", "id_fk_role"),
        ("tb_user_roles", "id_pfk_role"),
    ];

    /// <summary>Revisao 1 (ADR-0041): as 9 tabelas de tenant originais. Fixa: e a que a migration da ADR-0041 aplica.</summary>
    public const int RevisionAdr0041 = 1;

    /// <summary>Revisao 2 (ADR-0044): + <c>tb_tenant_admin_grants</c>.</summary>
    public const int RevisionAdr0044 = 2;

    /// <summary>Revisao 3 (ADR-0054): + <c>tb_authority_audit_entries</c>.</summary>
    public const int RevisionAdr0054 = 3;

    /// <summary>Revisao corrente — a que migrations futuras recriam depois de <see cref="Drop"/>.</summary>
    public const int CurrentRevision = RevisionAdr0054;

    /// <summary>
    /// Cria as politicas na revisao pedida. Migration ja aplicada chama a revisao dela, nunca a corrente:
    /// num banco novo, a tabela de uma revisao posterior ainda nao existe quando ela roda.
    /// </summary>
    /// <param name="migrationBuilder">Builder da migration.</param>
    /// <param name="revision">Revisao da politica.</param>
    public static void Create(MigrationBuilder migrationBuilder, int revision = CurrentRevision)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, RevisionAdr0041);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(revision, CurrentRevision);

        foreach (var table in TablesOf(revision))
        {
            migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
        }

        foreach (var (table, column) in DirectTablesOf(revision))
        {
            var predicate = $"{Mode} = 'platform' OR ({Mode} = 'tenant' AND {column} = {Tenant})";

            migrationBuilder.Sql(
                $"CREATE POLICY pol_tenant_isolation_{table} ON {table} USING ({predicate}) WITH CHECK ({predicate});");
        }

        foreach (var (table, column) in RoleBoundTables)
        {
            var predicate = $"{Mode} = 'platform' OR EXISTS (SELECT 1 FROM tb_roles r WHERE r.id_pk_role = {column} "
                + $"AND {Mode} = 'tenant' AND r.id_fk_tenant = {Tenant})";

            migrationBuilder.Sql(
                $"CREATE POLICY pol_tenant_isolation_{table} ON {table} USING ({predicate}) WITH CHECK ({predicate});");
        }

        // Pessoa (ADR-0040): LEITURA no tenant dono OU onde e membro; ESCRITA so pelo tenant dono (ou plataforma).
        // Duas politicas: pol_tenant_isolation_tb_users_select (FOR SELECT) e _write (FOR ALL: so concede escrita ao dono; a leitura de quem e so membro vem da _select, e as politicas permissivas se somam).
        var userReadPredicate = $"{Mode} = 'platform' OR ({Mode} = 'tenant' AND (id_fk_tenant = {Tenant} "
            + $"OR EXISTS (SELECT 1 FROM tb_tenant_memberships m WHERE m.id_pfk_user = id_pk_user AND m.id_pfk_tenant = {Tenant})))";
        var userWritePredicate = $"{Mode} = 'platform' OR ({Mode} = 'tenant' AND id_fk_tenant = {Tenant})";

        migrationBuilder.Sql(
            $"CREATE POLICY pol_tenant_isolation_tb_users_select ON tb_users FOR SELECT USING ({userReadPredicate});");
        migrationBuilder.Sql(
            $"CREATE POLICY pol_tenant_isolation_tb_users_write ON tb_users FOR ALL USING ({userWritePredicate}) WITH CHECK ({userWritePredicate});");
    }

    /// <summary>Remove as politicas e desabilita o RLS das tabelas protegidas na revisao pedida. Idempotente.</summary>
    /// <param name="migrationBuilder">Builder da migration.</param>
    /// <param name="revision">Revisao da politica (as tabelas dela precisam existir).</param>
    public static void Drop(MigrationBuilder migrationBuilder, int revision = CurrentRevision)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, RevisionAdr0041);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(revision, CurrentRevision);

        foreach (var table in TablesOf(revision))
        {
            migrationBuilder.Sql($"DROP POLICY IF EXISTS pol_tenant_isolation_{table} ON {table};");
            migrationBuilder.Sql($"DROP POLICY IF EXISTS pol_tenant_isolation_{table}_select ON {table};");
            migrationBuilder.Sql($"DROP POLICY IF EXISTS pol_tenant_isolation_{table}_write ON {table};");
            migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
        }
    }

    private static IEnumerable<string> TablesOf(int revision) =>
        DirectTablesOf(revision).Select(t => t.Table)
            .Concat(RoleBoundTables.Select(t => t.Table))
            .Append("tb_users");

    private static IEnumerable<(string Table, string Column)> DirectTablesOf(int revision)
    {
        IEnumerable<(string Table, string Column)> tables = DirectTenantTables;

        if (revision >= RevisionAdr0044)
        {
            tables = tables.Concat(DirectTenantTablesAdr0044);
        }

        if (revision >= RevisionAdr0054)
        {
            tables = tables.Concat(DirectTenantTablesAdr0054);
        }

        return tables;
    }
}
