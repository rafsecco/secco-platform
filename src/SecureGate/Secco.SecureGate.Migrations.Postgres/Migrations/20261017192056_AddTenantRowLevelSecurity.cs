using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.Postgres.Migrations
{
    /// <summary>
    /// Row-Level Security do banco de plataforma do SecureGate em PostgreSQL (ADR-0041, camada 2,
    /// issue #32), espelhando a migration SQL Server. Lê as configurações de sessão
    /// <c>secco.mode</c> (tenant | platform) e <c>secco.tenant</c> que o
    /// <c>TenantSessionContextInterceptor</c> grava na abertura da conexão.
    /// <list type="bullet">
    /// <item>Fail-closed: sem configuração (ou vazia) nenhuma linha passa.</item>
    /// <item><c>ENABLE</c> + <c>FORCE ROW LEVEL SECURITY</c>: a política vale também para a dona da
    /// tabela (a credencial de migração); só superusuário e <c>BYPASSRLS</c> escapam, e o runtime não pode ser nenhum deles.</item>
    /// <item>O Postgres exige um nome de política por tabela: <c>pol_tenant_isolation_&lt;tabela&gt;</c>
    /// (ex.: <c>pol_tenant_isolation_tb_roles</c>). <c>USING</c> filtra leitura/update/delete; <c>WITH CHECK</c>
    /// bloqueia escrita de linha de outro tenant (nas tabelas com tenant direto).</item>
    /// <item>Limite: não existe contexto somente leitura no Postgres — o RLS segura o filtro esquecido
    /// em SQL cru, não comandos empilhados (registrado na ADR-0041).</item>
    /// </list>
    /// </summary>
    public partial class AddTenantRowLevelSecurity : Migration
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

        // Atribuição e permissão: seguem o tenant do perfil referenciado
        private static readonly (string Table, string Column)[] RoleBoundTables =
        [
            ("tb_role_claims", "id_fk_role"),
            ("tb_user_roles", "id_pfk_role"),
        ];

        private static readonly string[] AllTables =
        [
            "tb_roles", "tb_role_claims", "tb_user_roles", "tb_tenant_memberships", "tb_users",
            "tb_tenant_databases", "tb_tenant_federations", "tb_tenant_group_role_mappings", "tb_elevation_grants",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AllTables)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
            }

            foreach (var (table, column) in DirectTenantTables)
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

            // Pessoa: vista no tenant dono OU onde é membro (ADR-0040). Sem WITH CHECK próprio além do
            // mesmo predicado: o tenant dono é gravado pelo UserManager no escopo do tenant.
            var userPredicate = $"{Mode} = 'platform' OR ({Mode} = 'tenant' AND (id_fk_tenant = {Tenant} "
                + $"OR EXISTS (SELECT 1 FROM tb_tenant_memberships m WHERE m.id_pfk_user = id_pk_user AND m.id_pfk_tenant = {Tenant})))";

            migrationBuilder.Sql(
                $"CREATE POLICY pol_tenant_isolation_tb_users ON tb_users USING ({userPredicate}) WITH CHECK ({userPredicate});");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AllTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS pol_tenant_isolation_{table} ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
