using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_permission_catalog_entries",
                columns: table => new
                {
                    id_pk_permission_catalog_entry = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_product = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ds_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ds_description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    dt_published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_catalog_entries", x => x.id_pk_permission_catalog_entry);
                });

            migrationBuilder.CreateIndex(
                name: "uk_permission_catalog_entries_ds_product_ds_name",
                table: "tb_permission_catalog_entries",
                columns: new[] { "ds_product", "ds_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_permission_catalog_entries");
        }
    }
}
