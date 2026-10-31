using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddBackgroundLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_background_leases",
                columns: table => new
                {
                    id_pk_background_lease = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ds_owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    dt_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_background_leases", x => x.id_pk_background_lease);
                });

            migrationBuilder.CreateIndex(
                name: "uk_background_leases_ds_name",
                table: "tb_background_leases",
                column: "ds_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_background_leases");
        }
    }
}
