using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class TenantFederations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_tenant_federations",
                columns: table => new
                {
                    id_pk_tenant_federation = table.Column<Guid>(type: "uuid", nullable: false),
                    id_fk_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    directory_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fl_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dt_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_federations", x => x.id_pk_tenant_federation);
                    table.ForeignKey(
                        name: "fk_tenant_federations_tenant",
                        column: x => x.id_fk_tenant,
                        principalTable: "tb_tenants",
                        principalColumn: "id_pk_tenant",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uk_tenant_federations_id_fk_tenant",
                table: "tb_tenant_federations",
                column: "id_fk_tenant",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_tenant_federations");
        }
    }
}
