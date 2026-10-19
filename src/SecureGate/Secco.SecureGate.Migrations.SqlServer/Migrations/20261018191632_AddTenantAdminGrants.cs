using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <summary>
    /// Concessões de admin de tenant (ADR-0044, issue #37): tabela <c>tb_tenant_admin_grants</c>, protegida
    /// pelo RLS da ADR-0041 como as demais tabelas de tenant — a política passa à revisão 2.
    /// </summary>
    public partial class AddTenantAdminGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_tenant_admin_grants",
                columns: table => new
                {
                    id_pk_tenant_admin_grant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_user = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_tenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_granted_by = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_admin_grants", x => x.id_pk_tenant_admin_grant);
                    table.ForeignKey(
                        name: "fk_tenant_admin_grants_tenant",
                        column: x => x.id_fk_tenant,
                        principalTable: "tb_tenants",
                        principalColumn: "id_pk_tenant",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tenant_admin_grants_user",
                        column: x => x.id_fk_user,
                        principalTable: "tb_users",
                        principalColumn: "id_pk_user",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_tenant_admin_grants_id_fk_user",
                table: "tb_tenant_admin_grants",
                column: "id_fk_user");

            migrationBuilder.CreateIndex(
                name: "uk_tenant_admin_grants_id_fk_tenant_id_fk_user",
                table: "tb_tenant_admin_grants",
                columns: new[] { "id_fk_tenant", "id_fk_user" },
                unique: true);

            TenantRowLevelSecurity.Drop(migrationBuilder);
            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0044);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A política é SCHEMABINDING: sai antes da tabela e volta na revisão anterior depois
            TenantRowLevelSecurity.Drop(migrationBuilder);

            migrationBuilder.DropTable(
                name: "tb_tenant_admin_grants");

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0041);
        }
    }
}
