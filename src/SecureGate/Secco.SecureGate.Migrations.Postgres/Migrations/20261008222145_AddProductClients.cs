using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProductClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ds_name",
                table: "tb_oidc_applications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "id_fk_tenant",
                table: "tb_oidc_applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ie_origin",
                table: "tb_oidc_applications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Clients existentes viram de plataforma (ie_origin = 0 pelo default) com Name = ClientId,
            // o que mantém o índice único (tenant, nome) sem colisão (ADR-0037).
            migrationBuilder.Sql("UPDATE tb_oidc_applications SET ds_name = ds_client_id WHERE ds_name IS NULL");

            migrationBuilder.CreateIndex(
                name: "uk_oidc_applications_id_fk_tenant_ds_name",
                table: "tb_oidc_applications",
                columns: new[] { "id_fk_tenant", "ds_name" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_oidc_applications_origin_tenant",
                table: "tb_oidc_applications",
                sql: "(ie_origin = 1 AND id_fk_tenant IS NOT NULL) OR (ie_origin = 0 AND id_fk_tenant IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_oidc_applications_tenant",
                table: "tb_oidc_applications",
                column: "id_fk_tenant",
                principalTable: "tb_tenants",
                principalColumn: "id_pk_tenant",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_oidc_applications_tenant",
                table: "tb_oidc_applications");

            migrationBuilder.DropIndex(
                name: "uk_oidc_applications_id_fk_tenant_ds_name",
                table: "tb_oidc_applications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_oidc_applications_origin_tenant",
                table: "tb_oidc_applications");

            migrationBuilder.DropColumn(
                name: "ds_name",
                table: "tb_oidc_applications");

            migrationBuilder.DropColumn(
                name: "id_fk_tenant",
                table: "tb_oidc_applications");

            migrationBuilder.DropColumn(
                name: "ie_origin",
                table: "tb_oidc_applications");
        }
    }
}
