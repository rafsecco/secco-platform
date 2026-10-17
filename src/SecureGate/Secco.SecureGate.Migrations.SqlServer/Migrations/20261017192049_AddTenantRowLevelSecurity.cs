using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <summary>
    /// Row-Level Security do banco de plataforma do SecureGate (ADR-0041, camada 2, issue #32). As 9
    /// tabelas de tenant ganham a política <c>pol_tenant_isolation</c> (prefixo <c>pol_</c>, ADR-0017),
    /// alimentada pelo contexto de sessão que o <c>TenantSessionContextInterceptor</c> grava na
    /// abertura da conexão (<c>secco_mode</c> = tenant | platform, <c>secco_tenant</c>).
    /// <list type="bullet">
    /// <item>Fail-closed: sem contexto de sessão, nenhuma linha passa.</item>
    /// <item>FILTER em todas as 9 tabelas; BLOCK AFTER INSERT/UPDATE nas que têm tenant direto
    /// (a mesma regra da checagem de escrita da rodada 1, agora no banco).</item>
    /// <item>As políticas valem também para <c>db_owner</c> e <c>sysadmin</c> (confirmado em teste).</item>
    /// <item>ATENÇÃO para migrations futuras: as funções e a política são <c>SCHEMABINDING</c>, então
    /// alterar/dropar colunas dessas tabelas (ou das referenciadas pelas funções) falha enquanto a
    /// política existir. Uma migration que mexa nelas precisa de <c>DROP SECURITY POLICY</c> antes e
    /// recriar a política depois (use o corpo do <c>Up</c> desta migration).</item>
    /// </list>
    /// </summary>
    public partial class AddTenantRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tabela com tenant direto: o tenant da linha é o do contexto (ou o contexto é plataforma).
            migrationBuilder.Sql(@"
CREATE FUNCTION dbo.fn_check_tenant_row(@id_tenant uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS fn_result
    WHERE CAST(SESSION_CONTEXT(N'secco_mode') AS nvarchar(16)) = N'platform'
       OR (CAST(SESSION_CONTEXT(N'secco_mode') AS nvarchar(16)) = N'tenant'
           AND @id_tenant = TRY_CAST(CAST(SESSION_CONTEXT(N'secco_tenant') AS nvarchar(36)) AS uniqueidentifier));
");

            // Tabela sem tenant próprio (atribuição e permissão): segue o tenant do PERFIL referenciado.
            migrationBuilder.Sql(@"
CREATE FUNCTION dbo.fn_check_role_row(@id_role uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS fn_result
    FROM dbo.tb_roles AS r
    WHERE r.id_pk_role = @id_role
      AND EXISTS (SELECT 1 FROM dbo.fn_check_tenant_row(r.id_fk_tenant));
");

            // Pessoa: vista no tenant dono OU onde é membro (ADR-0040).
            migrationBuilder.Sql(@"
CREATE FUNCTION dbo.fn_check_user_row(@id_user uniqueidentifier, @id_tenant uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS fn_result
    WHERE EXISTS (SELECT 1 FROM dbo.fn_check_tenant_row(@id_tenant))
       OR EXISTS (SELECT 1
                  FROM dbo.tb_tenant_memberships AS m
                  WHERE m.id_pfk_user = @id_user
                    AND EXISTS (SELECT 1 FROM dbo.fn_check_tenant_row(m.id_pfk_tenant)));
");

            migrationBuilder.Sql(@"
CREATE SECURITY POLICY dbo.pol_tenant_isolation
    ADD FILTER PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_roles,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_roles AFTER INSERT,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_roles AFTER UPDATE,

    ADD FILTER PREDICATE dbo.fn_check_role_row(id_fk_role) ON dbo.tb_role_claims,
    ADD FILTER PREDICATE dbo.fn_check_role_row(id_pfk_role) ON dbo.tb_user_roles,

    ADD FILTER PREDICATE dbo.fn_check_tenant_row(id_pfk_tenant) ON dbo.tb_tenant_memberships,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_pfk_tenant) ON dbo.tb_tenant_memberships AFTER INSERT,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_pfk_tenant) ON dbo.tb_tenant_memberships AFTER UPDATE,

    ADD FILTER PREDICATE dbo.fn_check_user_row(id_pk_user, id_fk_tenant) ON dbo.tb_users,

    ADD FILTER PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_tenant_databases,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_tenant_databases AFTER INSERT,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_tenant_databases AFTER UPDATE,

    ADD FILTER PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_tenant_federations,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_tenant_federations AFTER INSERT,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_tenant_federations AFTER UPDATE,

    ADD FILTER PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_tenant_group_role_mappings,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_tenant_group_role_mappings AFTER INSERT,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_tenant_group_role_mappings AFTER UPDATE,

    ADD FILTER PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_elevation_grants,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_elevation_grants AFTER INSERT,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_fk_tenant) ON dbo.tb_elevation_grants AFTER UPDATE
    WITH (STATE = ON, SCHEMABINDING = ON);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Ordem inversa: a política referencia as funções; fn_check_role_row/fn_check_user_row referenciam fn_check_tenant_row.
            migrationBuilder.Sql("DROP SECURITY POLICY dbo.pol_tenant_isolation;");
            migrationBuilder.Sql("DROP FUNCTION dbo.fn_check_user_row;");
            migrationBuilder.Sql("DROP FUNCTION dbo.fn_check_role_row;");
            migrationBuilder.Sql("DROP FUNCTION dbo.fn_check_tenant_row;");
        }
    }
}
