using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.NotificationHub.Migrations.SqlServer.Migrations
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
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "ie_channel",
                table: "tb_notifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "tb_channel_configurations",
                columns: table => new
                {
                    id_pk_channel_configuration = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_channel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ds_destination = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    fl_enabled = table.Column<bool>(type: "bit", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    dt_updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
