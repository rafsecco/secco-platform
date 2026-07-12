using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.LogStream.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_api_call_logs",
                columns: table => new
                {
                    id_pk_api_call_log = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_url = table.Column<string>(type: "text", nullable: false),
                    ds_http_method = table.Column<string>(type: "text", nullable: false),
                    fl_success = table.Column<bool>(type: "boolean", nullable: false),
                    ds_request_body = table.Column<string>(type: "text", nullable: true),
                    ds_request_headers = table.Column<string>(type: "text", nullable: true),
                    nr_response_status_code = table.Column<int>(type: "integer", nullable: true),
                    ds_response_body = table.Column<string>(type: "text", nullable: true),
                    nr_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    ds_error_message = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_call_logs", x => x.id_pk_api_call_log);
                });

            migrationBuilder.CreateTable(
                name: "tb_log_entries",
                columns: table => new
                {
                    id_pk_log_entry = table.Column<Guid>(type: "uuid", nullable: false),
                    ie_level = table.Column<int>(type: "integer", nullable: false),
                    ds_message = table.Column<string>(type: "text", nullable: false),
                    ds_stack_trace = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_log_entries", x => x.id_pk_log_entry);
                });

            migrationBuilder.CreateTable(
                name: "tb_log_processes",
                columns: table => new
                {
                    id_pk_log_process = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_name = table.Column<string>(type: "text", nullable: false),
                    ds_external_reference = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_log_processes", x => x.id_pk_log_process);
                });

            migrationBuilder.CreateTable(
                name: "tb_log_process_details",
                columns: table => new
                {
                    id_pk_log_process_detail = table.Column<Guid>(type: "uuid", nullable: false),
                    id_fk_log_process = table.Column<Guid>(type: "uuid", nullable: false),
                    ie_level = table.Column<int>(type: "integer", nullable: false),
                    ds_message = table.Column<string>(type: "text", nullable: false),
                    ds_stack_trace = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_log_process_details", x => x.id_pk_log_process_detail);
                    table.ForeignKey(
                        name: "fk_log_process_details_log_process",
                        column: x => x.id_fk_log_process,
                        principalTable: "tb_log_processes",
                        principalColumn: "id_pk_log_process",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateIndex(
                name: "idx_log_process_details_dt_created_at",
                table: "tb_log_process_details",
                column: "dt_created_at");

            migrationBuilder.CreateIndex(
                name: "idx_log_process_details_id_fk_log_process",
                table: "tb_log_process_details",
                column: "id_fk_log_process");

            migrationBuilder.CreateIndex(
                name: "idx_log_processes_correlation_id",
                table: "tb_log_processes",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "idx_log_processes_ds_name",
                table: "tb_log_processes",
                column: "ds_name");

            migrationBuilder.CreateIndex(
                name: "idx_log_processes_dt_created_at",
                table: "tb_log_processes",
                column: "dt_created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_api_call_logs");

            migrationBuilder.DropTable(
                name: "tb_log_entries");

            migrationBuilder.DropTable(
                name: "tb_log_process_details");

            migrationBuilder.DropTable(
                name: "tb_log_processes");
        }
    }
}
