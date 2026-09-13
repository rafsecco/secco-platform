using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddElevationGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_elevation_grants",
                columns: table => new
                {
                    id_pk_elevation_grant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_user = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_tenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_granted_by = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    dt_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_elevation_grants", x => x.id_pk_elevation_grant);
                    table.ForeignKey(
                        name: "fk_elevation_grants_tenant",
                        column: x => x.id_fk_tenant,
                        principalTable: "tb_tenants",
                        principalColumn: "id_pk_tenant",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_elevation_grants_user",
                        column: x => x.id_fk_user,
                        principalTable: "tb_users",
                        principalColumn: "id_pk_user",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_elevation_grants_id_fk_tenant",
                table: "tb_elevation_grants",
                column: "id_fk_tenant");

            migrationBuilder.CreateIndex(
                name: "uk_elevation_grants_id_fk_user",
                table: "tb_elevation_grants",
                column: "id_fk_user",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_elevation_grants");
        }
    }
}
