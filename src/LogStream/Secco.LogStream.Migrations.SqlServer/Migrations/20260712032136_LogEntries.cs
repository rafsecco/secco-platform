using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.LogStream.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class LogEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_log_entries",
                columns: table => new
                {
                    id_pk_log_entry = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ie_level = table.Column<int>(type: "int", nullable: false),
                    ds_message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ds_stack_trace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_log_entries", x => x.id_pk_log_entry);
                });

            migrationBuilder.CreateIndex(
                name: "idx_log_entries_correlation_id",
                table: "tb_log_entries",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "idx_log_entries_dt_created_at",
                table: "tb_log_entries",
                column: "dt_created_at");

            migrationBuilder.CreateIndex(
                name: "idx_log_entries_dt_created_at_ie_level",
                table: "tb_log_entries",
                columns: new[] { "dt_created_at", "ie_level" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_log_entries");
        }
    }
}
