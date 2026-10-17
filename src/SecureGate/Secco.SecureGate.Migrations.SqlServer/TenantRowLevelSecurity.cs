using Microsoft.EntityFrameworkCore.Migrations;

namespace Secco.SecureGate.Migrations.SqlServer;

/// <summary>
/// SQL do Row-Level Security do banco de plataforma do SecureGate (ADR-0041, camada 2): funcoes de predicado
/// <c>fn_check_*</c> e a politica <c>pol_tenant_isolation</c> nas 9 tabelas de tenant.
/// <para>
/// ATENCAO para migrations futuras: funcoes e politica sao <c>SCHEMABINDING</c>, entao alterar ou dropar
/// colunas das tabelas protegidas (ou das referenciadas pelas funcoes) falha enquanto a politica existir.
/// Uma migration que mexa nelas deve chamar <see cref="Drop"/> no comeco e <see cref="Create"/> no fim
/// (a versao corrente da politica; se o conjunto de tabelas/predicados mudar, atualize este metodo e
/// o teste de contagem de predicados).
/// </para>
/// </summary>
public static class TenantRowLevelSecurity
{
    /// <summary>Remove a politica e as funcoes (ordem inversa das dependencias). Idempotente.</summary>
    public static void Drop(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY IF EXISTS dbo.pol_tenant_isolation;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.fn_check_user_row;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.fn_check_role_row;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.fn_check_tenant_row;");
    }

    /// <summary>Cria as funcoes de predicado e a politica (FILTER e BLOCK em todas as tabelas protegidas).</summary>
    public static void Create(MigrationBuilder migrationBuilder)
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
    ADD BLOCK  PREDICATE dbo.fn_check_role_row(id_fk_role) ON dbo.tb_role_claims AFTER INSERT,
    ADD BLOCK  PREDICATE dbo.fn_check_role_row(id_fk_role) ON dbo.tb_role_claims AFTER UPDATE,
    ADD FILTER PREDICATE dbo.fn_check_role_row(id_pfk_role) ON dbo.tb_user_roles,
    ADD BLOCK  PREDICATE dbo.fn_check_role_row(id_pfk_role) ON dbo.tb_user_roles AFTER INSERT,
    ADD BLOCK  PREDICATE dbo.fn_check_role_row(id_pfk_role) ON dbo.tb_user_roles AFTER UPDATE,

    ADD FILTER PREDICATE dbo.fn_check_tenant_row(id_pfk_tenant) ON dbo.tb_tenant_memberships,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_pfk_tenant) ON dbo.tb_tenant_memberships AFTER INSERT,
    ADD BLOCK  PREDICATE dbo.fn_check_tenant_row(id_pfk_tenant) ON dbo.tb_tenant_memberships AFTER UPDATE,

    ADD FILTER PREDICATE dbo.fn_check_user_row(id_pk_user, id_fk_tenant) ON dbo.tb_users,
    ADD BLOCK  PREDICATE dbo.fn_check_user_row(id_pk_user, id_fk_tenant) ON dbo.tb_users AFTER INSERT,
    ADD BLOCK  PREDICATE dbo.fn_check_user_row(id_pk_user, id_fk_tenant) ON dbo.tb_users AFTER UPDATE,

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
}
