using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.Postgres.Migrations
{
    /// <summary>
    /// Criação de tenant pelo admin de tenant (ADR-0047, issue #39): <c>tb_tenant_creator_grants</c> (dado de
    /// plataforma, sem RLS) e a marca <c>fl_created_tenant</c> na concessão de admin, que conta para o limite por
    /// pessoa. A coluna nova entra numa tabela protegida, então a política sai antes e volta depois (ADR-0041).
    /// </summary>
    public partial class AddTenantCreatorGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0044);

            migrationBuilder.AddColumn<bool>(
                name: "fl_created_tenant",
                table: "tb_tenant_admin_grants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "tb_tenant_creator_grants",
                columns: table => new
                {
                    id_pk_tenant_creator_grant = table.Column<Guid>(type: "uuid", nullable: false),
                    id_fk_user = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_granted_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_creator_grants", x => x.id_pk_tenant_creator_grant);
                    table.ForeignKey(
                        name: "fk_tenant_creator_grants_user",
                        column: x => x.id_fk_user,
                        principalTable: "tb_users",
                        principalColumn: "id_pk_user",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uk_tenant_creator_grants_id_fk_user",
                table: "tb_tenant_creator_grants",
                column: "id_fk_user",
                unique: true);
        

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0044);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0044);

            migrationBuilder.DropTable(
                name: "tb_tenant_creator_grants");

            migrationBuilder.DropColumn(
                name: "fl_created_tenant",
                table: "tb_tenant_admin_grants");
        

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0044);
        }
    }
}
