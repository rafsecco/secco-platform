using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.LogStream.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AuditAndLogEntryEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ds_category",
                table: "tb_log_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ds_service_name",
                table: "tb_log_entries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tb_audit_entries",
                columns: table => new
                {
                    id_pk_audit_entry = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_actor_id = table.Column<string>(type: "text", nullable: false),
                    ds_actor_name = table.Column<string>(type: "text", nullable: true),
                    ie_actor_type = table.Column<int>(type: "integer", nullable: false),
                    ds_action = table.Column<string>(type: "text", nullable: false),
                    ds_resource_type = table.Column<string>(type: "text", nullable: true),
                    ds_resource_id = table.Column<string>(type: "text", nullable: true),
                    ds_metadata = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dt_occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id_pk_audit_entry);
                });

            migrationBuilder.CreateIndex(
                name: "idx_log_entries_ds_service_name",
                table: "tb_log_entries",
                column: "ds_service_name");

            migrationBuilder.CreateIndex(
                name: "idx_audit_entries_ds_actor_id_dt_occurred_at",
                table: "tb_audit_entries",
                columns: new[] { "ds_actor_id", "dt_occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_audit_entries_ds_resource_type_ds_resource_id",
                table: "tb_audit_entries",
                columns: new[] { "ds_resource_type", "ds_resource_id" });

            migrationBuilder.CreateIndex(
                name: "idx_audit_entries_dt_occurred_at",
                table: "tb_audit_entries",
                column: "dt_occurred_at",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_audit_entries");

            migrationBuilder.DropIndex(
                name: "idx_log_entries_ds_service_name",
                table: "tb_log_entries");

            migrationBuilder.DropColumn(
                name: "ds_category",
                table: "tb_log_entries");

            migrationBuilder.DropColumn(
                name: "ds_service_name",
                table: "tb_log_entries");
        }
    }
}
