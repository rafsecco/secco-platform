using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.NotificationHub.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_notifications",
                columns: table => new
                {
                    id_pk_notification = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_recipient = table.Column<string>(type: "text", nullable: false),
                    ds_subject = table.Column<string>(type: "text", nullable: false),
                    ds_body = table.Column<string>(type: "text", nullable: false),
                    ie_status = table.Column<int>(type: "integer", nullable: false),
                    ds_failure_reason = table.Column<string>(type: "text", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dt_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id_pk_notification);
                });

            migrationBuilder.CreateIndex(
                name: "idx_notifications_dt_created_at",
                table: "tb_notifications",
                column: "dt_created_at");

            migrationBuilder.CreateIndex(
                name: "idx_notifications_ie_status",
                table: "tb_notifications",
                column: "ie_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_notifications");
        }
    }
}
