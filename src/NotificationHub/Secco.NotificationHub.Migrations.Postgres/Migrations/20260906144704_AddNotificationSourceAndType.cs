using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.NotificationHub.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSourceAndType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ds_source",
                table: "tb_notifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ds_type",
                table: "tb_notifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_notifications_ds_source_ds_type",
                table: "tb_notifications",
                columns: new[] { "ds_source", "ds_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_notifications_ds_source_ds_type",
                table: "tb_notifications");

            migrationBuilder.DropColumn(
                name: "ds_source",
                table: "tb_notifications");

            migrationBuilder.DropColumn(
                name: "ds_type",
                table: "tb_notifications");
        }
    }
}
