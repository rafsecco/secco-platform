using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.SecureGate.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_oidc_applications",
                columns: table => new
                {
                    id_pk_oidc_application = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_application_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ds_client_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ds_client_secret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_client_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ds_concurrency_token = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ds_consent_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ds_display_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_display_names = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_json_web_key_set = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_permissions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_post_logout_redirect_uris = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_redirect_uris = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_requirements = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_settings = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_applications", x => x.id_pk_oidc_application);
                });

            migrationBuilder.CreateTable(
                name: "tb_oidc_scopes",
                columns: table => new
                {
                    id_pk_oidc_scope = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_concurrency_token = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ds_description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_descriptions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_display_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_display_names = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ds_properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_resources = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_scopes", x => x.id_pk_oidc_scope);
                });

            migrationBuilder.CreateTable(
                name: "tb_tenants",
                columns: table => new
                {
                    id_pk_tenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ds_slug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    fl_active = table.Column<bool>(type: "bit", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id_pk_tenant);
                });

            migrationBuilder.CreateTable(
                name: "tb_oidc_authorizations",
                columns: table => new
                {
                    id_pk_oidc_authorization = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_application = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ds_concurrency_token = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    dt_creation_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ds_properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_scopes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ds_subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ds_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_authorizations", x => x.id_pk_oidc_authorization);
                    table.ForeignKey(
                        name: "fk_oidc_authorizations_oidc_application",
                        column: x => x.id_fk_application,
                        principalTable: "tb_oidc_applications",
                        principalColumn: "id_pk_oidc_application");
                });

            migrationBuilder.CreateTable(
                name: "tb_roles",
                columns: table => new
                {
                    id_pk_role = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_tenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ds_normalized_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ds_concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id_pk_role);
                    table.ForeignKey(
                        name: "fk_roles_tenant",
                        column: x => x.id_fk_tenant,
                        principalTable: "tb_tenants",
                        principalColumn: "id_pk_tenant",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tb_users",
                columns: table => new
                {
                    id_pk_user = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_tenant = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ds_normalized_user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ds_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ds_normalized_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    fl_email_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    ds_password_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_security_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_phone_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    fl_phone_number_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    fl_two_factor_enabled = table.Column<bool>(type: "bit", nullable: false),
                    dt_lockout_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    fl_lockout_enabled = table.Column<bool>(type: "bit", nullable: false),
                    nr_access_failed_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id_pk_user);
                    table.ForeignKey(
                        name: "fk_users_tenant",
                        column: x => x.id_fk_tenant,
                        principalTable: "tb_tenants",
                        principalColumn: "id_pk_tenant",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tb_oidc_tokens",
                columns: table => new
                {
                    id_pk_oidc_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_application = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    id_fk_authorization = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ds_concurrency_token = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    dt_creation_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    dt_expiration_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ds_payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    dt_redemption_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ds_reference_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ds_status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ds_subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ds_type = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_tokens", x => x.id_pk_oidc_token);
                    table.ForeignKey(
                        name: "fk_oidc_tokens_oidc_application",
                        column: x => x.id_fk_application,
                        principalTable: "tb_oidc_applications",
                        principalColumn: "id_pk_oidc_application");
                    table.ForeignKey(
                        name: "fk_oidc_tokens_oidc_authorization",
                        column: x => x.id_fk_authorization,
                        principalTable: "tb_oidc_authorizations",
                        principalColumn: "id_pk_oidc_authorization");
                });

            migrationBuilder.CreateTable(
                name: "tb_role_claims",
                columns: table => new
                {
                    id_pk_role_claim = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    id_fk_role = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id_pk_role_claim);
                    table.ForeignKey(
                        name: "fk_role_claims_role",
                        column: x => x.id_fk_role,
                        principalTable: "tb_roles",
                        principalColumn: "id_pk_role",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tb_user_claims",
                columns: table => new
                {
                    id_pk_user_claim = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    id_fk_user = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ds_claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id_pk_user_claim);
                    table.ForeignKey(
                        name: "fk_user_claims_user",
                        column: x => x.id_fk_user,
                        principalTable: "tb_users",
                        principalColumn: "id_pk_user",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tb_user_logins",
                columns: table => new
                {
                    id_pk_login_provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_pk_provider_key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ds_provider_display_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    id_fk_user = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.id_pk_login_provider, x.id_pk_provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_user",
                        column: x => x.id_fk_user,
                        principalTable: "tb_users",
                        principalColumn: "id_pk_user",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tb_user_roles",
                columns: table => new
                {
                    id_pfk_user = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_pfk_role = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.id_pfk_user, x.id_pfk_role });
                    table.ForeignKey(
                        name: "fk_user_roles_role",
                        column: x => x.id_pfk_role,
                        principalTable: "tb_roles",
                        principalColumn: "id_pk_role",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_user",
                        column: x => x.id_pfk_user,
                        principalTable: "tb_users",
                        principalColumn: "id_pk_user",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tb_user_tokens",
                columns: table => new
                {
                    id_pfk_user = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_pk_login_provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    id_pk_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ds_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.id_pfk_user, x.id_pk_login_provider, x.id_pk_name });
                    table.ForeignKey(
                        name: "fk_user_tokens_user",
                        column: x => x.id_pfk_user,
                        principalTable: "tb_users",
                        principalColumn: "id_pk_user",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uk_oidc_applications_ds_client_id",
                table: "tb_oidc_applications",
                column: "ds_client_id",
                unique: true,
                filter: "[ds_client_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_oidc_authorizations_id_fk_application_ds_status_ds_subject_ds_type",
                table: "tb_oidc_authorizations",
                columns: new[] { "id_fk_application", "ds_status", "ds_subject", "ds_type" });

            migrationBuilder.CreateIndex(
                name: "uk_oidc_scopes_ds_name",
                table: "tb_oidc_scopes",
                column: "ds_name",
                unique: true,
                filter: "[ds_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_oidc_tokens_id_fk_application_ds_status_ds_subject_ds_type",
                table: "tb_oidc_tokens",
                columns: new[] { "id_fk_application", "ds_status", "ds_subject", "ds_type" });

            migrationBuilder.CreateIndex(
                name: "idx_oidc_tokens_id_fk_authorization",
                table: "tb_oidc_tokens",
                column: "id_fk_authorization");

            migrationBuilder.CreateIndex(
                name: "uk_oidc_tokens_ds_reference_id",
                table: "tb_oidc_tokens",
                column: "ds_reference_id",
                unique: true,
                filter: "[ds_reference_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_role_claims_id_fk_role",
                table: "tb_role_claims",
                column: "id_fk_role");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "tb_roles",
                column: "ds_normalized_name",
                unique: true,
                filter: "[ds_normalized_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uk_roles_id_fk_tenant_ds_normalized_name",
                table: "tb_roles",
                columns: new[] { "id_fk_tenant", "ds_normalized_name" },
                unique: true,
                filter: "[ds_normalized_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uk_tenants_ds_slug",
                table: "tb_tenants",
                column: "ds_slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_user_claims_id_fk_user",
                table: "tb_user_claims",
                column: "id_fk_user");

            migrationBuilder.CreateIndex(
                name: "idx_user_logins_id_fk_user",
                table: "tb_user_logins",
                column: "id_fk_user");

            migrationBuilder.CreateIndex(
                name: "idx_user_roles_id_pfk_role",
                table: "tb_user_roles",
                column: "id_pfk_role");

            migrationBuilder.CreateIndex(
                name: "idx_users_ds_normalized_email",
                table: "tb_users",
                column: "ds_normalized_email");

            migrationBuilder.CreateIndex(
                name: "idx_users_id_fk_tenant",
                table: "tb_users",
                column: "id_fk_tenant");

            migrationBuilder.CreateIndex(
                name: "uk_users_ds_normalized_user_name",
                table: "tb_users",
                column: "ds_normalized_user_name",
                unique: true,
                filter: "[ds_normalized_user_name] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_oidc_scopes");

            migrationBuilder.DropTable(
                name: "tb_oidc_tokens");

            migrationBuilder.DropTable(
                name: "tb_role_claims");

            migrationBuilder.DropTable(
                name: "tb_user_claims");

            migrationBuilder.DropTable(
                name: "tb_user_logins");

            migrationBuilder.DropTable(
                name: "tb_user_roles");

            migrationBuilder.DropTable(
                name: "tb_user_tokens");

            migrationBuilder.DropTable(
                name: "tb_oidc_authorizations");

            migrationBuilder.DropTable(
                name: "tb_roles");

            migrationBuilder.DropTable(
                name: "tb_users");

            migrationBuilder.DropTable(
                name: "tb_oidc_applications");

            migrationBuilder.DropTable(
                name: "tb_tenants");
        }
    }
}
