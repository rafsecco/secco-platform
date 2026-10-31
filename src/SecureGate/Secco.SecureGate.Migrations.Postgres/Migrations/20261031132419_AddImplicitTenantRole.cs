using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.Postgres.Migrations
{
    /// <summary>
    /// Perfil implícito do tenant (ADR-0056): marca <c>fl_implicit</c> em <c>tb_roles</c> e índice único filtrado
    /// (no máximo um por tenant). A tabela é protegida pelo RLS da ADR-0041, então a política sai antes e volta
    /// depois, na revisão da ADR-0054.
    /// </summary>
    public partial class AddImplicitTenantRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder);

            migrationBuilder.AddColumn<bool>(
                name: "fl_implicit",
                table: "tb_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "uk_roles_id_fk_tenant_implicit",
                table: "tb_roles",
                column: "id_fk_tenant",
                unique: true,
                filter: "fl_implicit");

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder);

            migrationBuilder.DropIndex(
                name: "uk_roles_id_fk_tenant_implicit",
                table: "tb_roles");

            migrationBuilder.DropColumn(
                name: "fl_implicit",
                table: "tb_roles");

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054);
        }
    }
}
