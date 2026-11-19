using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <summary>
    /// Login federado direto por tenant: <c>fl_direct_sign_in</c> em <c>tb_tenant_federations</c> (opt-in, padrão
    /// falso). A tabela é protegida pelo RLS da ADR-0041, então a política sai antes e volta depois, na revisão da
    /// ADR-0054.
    /// </summary>
    public partial class AddFederationDirectSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder);

            migrationBuilder.AddColumn<bool>(
                name: "fl_direct_sign_in",
                table: "tb_tenant_federations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder);

            migrationBuilder.DropColumn(
                name: "fl_direct_sign_in",
                table: "tb_tenant_federations");

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054);
        }
    }
}
