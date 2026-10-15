using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_tenant_memberships",
                columns: table => new
                {
                    id_pfk_user = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_pfk_tenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fl_active = table.Column<bool>(type: "bit", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_memberships", x => new { x.id_pfk_user, x.id_pfk_tenant });
                    table.ForeignKey(
                        name: "fk_tenant_memberships_tenant",
                        column: x => x.id_pfk_tenant,
                        principalTable: "tb_tenants",
                        principalColumn: "id_pk_tenant",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_memberships_user",
                        column: x => x.id_pfk_user,
                        principalTable: "tb_users",
                        principalColumn: "id_pk_user",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_tenant_memberships_id_pfk_tenant",
                table: "tb_tenant_memberships",
                column: "id_pfk_tenant");

            // ADR-0040: cada usuário existente ganha o vínculo ativo com o próprio tenant (o tenant
            // dono). Sem isso a emissão de token, que passa a exigir vínculo, recusaria quem já existe.
            migrationBuilder.Sql(
                "INSERT INTO tb_tenant_memberships (id_pfk_user, id_pfk_tenant, fl_active, dt_created_at) " +
                "SELECT id_pk_user, id_fk_tenant, 1, SYSDATETIMEOFFSET() FROM tb_users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_tenant_memberships");
        }
    }
}
