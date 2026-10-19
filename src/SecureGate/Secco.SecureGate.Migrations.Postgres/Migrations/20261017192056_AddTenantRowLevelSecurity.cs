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
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Revisão fixa: num banco novo, as tabelas de revisões posteriores ainda não existem aqui
            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0041);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0041);
        }
    }
}
