using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.NotificationHub.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_in_app_notifications_user_id_fl_read",
                table: "tb_in_app_notifications");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dt_scheduled_for",
                table: "tb_notifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dt_scheduled_for",
                table: "tb_in_app_notifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_notifications_dt_scheduled_for",
                table: "tb_notifications",
                column: "dt_scheduled_for");

            migrationBuilder.CreateIndex(
                name: "idx_in_app_notifications_user_id_fl_read_dt_scheduled_for",
                table: "tb_in_app_notifications",
                columns: new[] { "user_id", "fl_read", "dt_scheduled_for" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_notifications_dt_scheduled_for",
                table: "tb_notifications");

            migrationBuilder.DropIndex(
                name: "idx_in_app_notifications_user_id_fl_read_dt_scheduled_for",
                table: "tb_in_app_notifications");

            migrationBuilder.DropColumn(
                name: "dt_scheduled_for",
                table: "tb_notifications");

            migrationBuilder.DropColumn(
                name: "dt_scheduled_for",
                table: "tb_in_app_notifications");

            migrationBuilder.CreateIndex(
                name: "idx_in_app_notifications_user_id_fl_read",
                table: "tb_in_app_notifications",
                columns: new[] { "user_id", "fl_read" });
        }
    }
}
