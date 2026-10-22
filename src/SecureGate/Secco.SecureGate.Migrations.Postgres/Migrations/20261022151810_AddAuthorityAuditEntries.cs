using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.Postgres.Migrations
{
    /// <summary>
    /// Trilha de autoridade guardada no SecureGate (ADR-0054, issue #48): <c>tb_authority_audit_entries</c>, dado de
    /// tenant protegido pelo RLS da ADR-0041 — a política passa à revisão 3.
    /// </summary>
    public partial class AddAuthorityAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_authority_audit_entries",
                columns: table => new
                {
                    id_pk_authority_audit_entry = table.Column<Guid>(type: "uuid", nullable: false),
                    id_fk_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_actor_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ds_actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ds_actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ds_action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ds_resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ds_resource_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ds_metadata = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authority_audit_entries", x => x.id_pk_authority_audit_entry);
                    table.ForeignKey(
                        name: "fk_authority_audit_entries_tenant",
                        column: x => x.id_fk_tenant,
                        principalTable: "tb_tenants",
                        principalColumn: "id_pk_tenant",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_authority_audit_entries_dt_created_at",
                table: "tb_authority_audit_entries",
                column: "dt_created_at");

            migrationBuilder.CreateIndex(
                name: "idx_authority_audit_entries_id_fk_tenant_dt_created_at",
                table: "tb_authority_audit_entries",
                columns: new[] { "id_fk_tenant", "dt_created_at" },
                descending: new[] { false, true });

            TenantRowLevelSecurity.Drop(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0044);
            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            TenantRowLevelSecurity.Drop(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054);

            migrationBuilder.DropTable(
                name: "tb_authority_audit_entries");

            TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0044);
        }
    }
}
