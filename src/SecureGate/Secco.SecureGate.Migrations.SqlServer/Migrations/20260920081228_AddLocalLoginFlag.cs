using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalLoginFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default true: linhas existentes continuam com login local, e não perdem acesso
            // silenciosamente na migração (ADR-0033).
            migrationBuilder.AddColumn<bool>(
                name: "fl_local_login_enabled",
                table: "tb_users",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fl_local_login_enabled",
                table: "tb_users");
        }
    }
}
