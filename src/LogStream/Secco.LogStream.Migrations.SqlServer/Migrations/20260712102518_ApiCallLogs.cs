using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.LogStream.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class ApiCallLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_api_call_logs",
                columns: table => new
                {
                    id_pk_api_call_log = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ds_http_method = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    fl_success = table.Column<bool>(type: "bit", nullable: false),
                    ds_request_body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_request_headers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    nr_response_status_code = table.Column<int>(type: "int", nullable: true),
                    ds_response_body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    nr_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    ds_error_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_call_logs", x => x.id_pk_api_call_log);
                });

            migrationBuilder.CreateIndex(
                name: "idx_api_call_logs_correlation_id",
                table: "tb_api_call_logs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "idx_api_call_logs_dt_created_at",
                table: "tb_api_call_logs",
                column: "dt_created_at");

            migrationBuilder.CreateIndex(
                name: "idx_api_call_logs_dt_created_at_fl_success",
                table: "tb_api_call_logs",
                columns: new[] { "dt_created_at", "fl_success" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_api_call_logs");
        }
    }
}
