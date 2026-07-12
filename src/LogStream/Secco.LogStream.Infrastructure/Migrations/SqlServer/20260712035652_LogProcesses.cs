using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.LogStream.Infrastructure.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class LogProcesses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_log_processes",
                columns: table => new
                {
                    id_pk_log_process = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ds_external_reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_log_processes", x => x.id_pk_log_process);
                });

            migrationBuilder.CreateTable(
                name: "tb_log_process_details",
                columns: table => new
                {
                    id_pk_log_process_detail = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_log_process = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ie_level = table.Column<int>(type: "int", nullable: false),
                    ds_message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ds_stack_trace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                name: "tb_log_process_details");

            migrationBuilder.DropTable(
                name: "tb_log_processes");
        }
    }
}
