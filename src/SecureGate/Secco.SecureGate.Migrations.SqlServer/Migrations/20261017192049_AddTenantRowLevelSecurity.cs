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
    /// <item>FILTER em todas as 9 tabelas; BLOCK AFTER INSERT/UPDATE nas 9 (a mesma regra da checagem de escrita da rodada 1, agora no banco).</item>
    /// <item>As políticas valem também para <c>db_owner</c> e <c>sysadmin</c> (confirmado em teste).</item>
    /// <item>ATENÇÃO para migrations futuras: as funções e a política são <c>SCHEMABINDING</c>, então
    /// alterar/dropar colunas dessas tabelas (ou das referenciadas pelas funções) falha enquanto a
    /// política existir. Uma migration que mexa nelas chama <c>TenantRowLevelSecurity.Drop</c> no começo e
    /// <c>TenantRowLevelSecurity.Create</c> no fim.</item>
    /// </list>
    /// </summary>
    public partial class AddTenantRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Create(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder);
        }
    }
}
