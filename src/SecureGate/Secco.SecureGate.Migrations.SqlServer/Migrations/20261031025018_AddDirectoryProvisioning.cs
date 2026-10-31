using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <summary>
    /// Provisionamento de contas pelo diretório federado (ADR-0055, entrega B): conta gerida e origem da desativação
    /// em <c>tb_users</c>, grupo de provisionamento em <c>tb_tenant_group_role_mappings</c> (no máximo um por tenant,
    /// por índice único filtrado) e domínios de e-mail permitidos em <c>tb_tenant_federations</c>. As três tabelas são
    /// protegidas pelo RLS da ADR-0041, então a política sai antes e volta depois, como manda a regra.
    /// </summary>
    public partial class AddDirectoryProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder);

            migrationBuilder.AddColumn<bool>(
                name: "fl_directory_managed",
                table: "tb_users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ie_deactivation_origin",
                table: "tb_users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "fl_provisioning_group",
                table: "tb_tenant_group_role_mappings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ds_allowed_email_domains",
                table: "tb_tenant_federations",
                type: "nvarchar(2539)",
                maxLength: 2539,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "uk_tenant_group_role_mappings_id_fk_tenant_provisioning",
                table: "tb_tenant_group_role_mappings",
                column: "id_fk_tenant",
                unique: true,
                filter: "[fl_provisioning_group] = 1");

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder);

            migrationBuilder.DropIndex(
                name: "uk_tenant_group_role_mappings_id_fk_tenant_provisioning",
                table: "tb_tenant_group_role_mappings");

            migrationBuilder.DropColumn(
                name: "fl_directory_managed",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "ie_deactivation_origin",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "fl_provisioning_group",
                table: "tb_tenant_group_role_mappings");

            migrationBuilder.DropColumn(
                name: "ds_allowed_email_domains",
                table: "tb_tenant_federations");

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054);
        }
    }
}
