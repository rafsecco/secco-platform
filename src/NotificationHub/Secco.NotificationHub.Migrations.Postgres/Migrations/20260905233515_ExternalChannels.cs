using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.NotificationHub.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ExternalChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ds_recipient",
                table: "tb_notifications",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "ie_channel",
                table: "tb_notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "tb_channel_configurations",
                columns: table => new
                {
                    id_pk_channel_configuration = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ds_destination = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    fl_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dt_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_configurations", x => x.id_pk_channel_configuration);
                });

            migrationBuilder.CreateIndex(
                name: "uk_channel_configurations_ds_channel",
                table: "tb_channel_configurations",
                column: "ds_channel",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_channel_configurations");

            migrationBuilder.DropColumn(
                name: "ie_channel",
                table: "tb_notifications");

            migrationBuilder.AlterColumn<string>(
                name: "ds_recipient",
                table: "tb_notifications",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
