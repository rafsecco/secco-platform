using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.NotificationHub.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_samples",
                columns: table => new
                {
                    id_pk_sample = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ds_description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_samples", x => x.id_pk_sample);
                });

            migrationBuilder.CreateIndex(
                name: "idx_samples_ds_name",
                table: "tb_samples",
                column: "ds_name");

            migrationBuilder.CreateIndex(
                name: "idx_samples_dt_created_at",
                table: "tb_samples",
                column: "dt_created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_samples");
        }
    }
}
