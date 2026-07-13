using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class TenantCatalogDatabases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_tenant_databases",
                columns: table => new
                {
                    id_pk_tenant_database = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_tenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_product = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ds_connection_string = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    dt_updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_databases", x => x.id_pk_tenant_database);
                    table.ForeignKey(
                        name: "fk_tenant_databases_tenant",
                        column: x => x.id_fk_tenant,
                        principalTable: "tb_tenants",
                        principalColumn: "id_pk_tenant",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uk_tenant_databases_id_fk_tenant_ds_product",
                table: "tb_tenant_databases",
                columns: new[] { "id_fk_tenant", "ds_product" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_tenant_databases");
        }
    }
}
