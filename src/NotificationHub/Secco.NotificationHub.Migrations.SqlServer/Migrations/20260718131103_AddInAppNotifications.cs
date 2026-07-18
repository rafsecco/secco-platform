using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.NotificationHub.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddInAppNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_in_app_notifications",
                columns: table => new
                {
                    id_pk_in_app_notification = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ds_type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ds_title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ds_message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ds_link = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    fl_read = table.Column<bool>(type: "bit", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    dt_read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_in_app_notifications", x => x.id_pk_in_app_notification);
                });

            migrationBuilder.CreateIndex(
                name: "idx_in_app_notifications_user_id_fl_read",
                table: "tb_in_app_notifications",
                columns: new[] { "user_id", "fl_read" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_in_app_notifications");
        }
    }
}
