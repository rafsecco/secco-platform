using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupRoleMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ie_origin",
                table: "tb_user_roles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "source_group_id",
                table: "tb_user_roles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tb_tenant_group_role_mappings",
                columns: table => new
                {
                    id_pk_tenant_group_role_mapping = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_tenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    entra_group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_entra_group_display_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    id_fk_role = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_group_role_mappings", x => x.id_pk_tenant_group_role_mapping);
                    table.ForeignKey(
                        name: "fk_tenant_group_role_mappings_role",
                        column: x => x.id_fk_role,
                        principalTable: "tb_roles",
                        principalColumn: "id_pk_role",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_group_role_mappings_tenant",
                        column: x => x.id_fk_tenant,
                        principalTable: "tb_tenants",
                        principalColumn: "id_pk_tenant",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_tenant_group_role_mappings_id_fk_role",
                table: "tb_tenant_group_role_mappings",
                column: "id_fk_role");

            migrationBuilder.CreateIndex(
                name: "uk_tenant_group_role_mappings_id_fk_tenant_entra_group_id",
                table: "tb_tenant_group_role_mappings",
                columns: new[] { "id_fk_tenant", "entra_group_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_tenant_group_role_mappings");

            migrationBuilder.DropColumn(
                name: "ie_origin",
                table: "tb_user_roles");

            migrationBuilder.DropColumn(
                name: "source_group_id",
                table: "tb_user_roles");
        }
    }
}
