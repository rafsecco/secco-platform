using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <summary>
    /// Atributos de pessoa vindos do diretório federado (ADR-0055): cargo, departamento, telefone comercial, gestor
    /// (auto-FK sem ação) e origem em <c>tb_users</c>. A tabela é protegida pelo RLS da ADR-0041, então a política
    /// sai antes e volta depois, na revisão da ADR-0054.
    /// </summary>
    public partial class AddDirectoryAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "ds_business_phone",
                table: "tb_users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ds_department",
                table: "tb_users",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ds_job_title",
                table: "tb_users",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "id_fk_manager",
                table: "tb_users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ie_attributes_origin",
                table: "tb_users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "idx_users_id_fk_manager",
                table: "tb_users",
                column: "id_fk_manager");

            migrationBuilder.AddForeignKey(
                name: "fk_users_user",
                table: "tb_users",
                column: "id_fk_manager",
                principalTable: "tb_users",
                principalColumn: "id_pk_user");

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "fk_users_user",
                table: "tb_users");

            migrationBuilder.DropIndex(
                name: "idx_users_id_fk_manager",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "ds_business_phone",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "ds_department",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "ds_job_title",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "id_fk_manager",
                table: "tb_users");

            migrationBuilder.DropColumn(
                name: "ie_attributes_origin",
                table: "tb_users");

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054);
        }
    }
}
