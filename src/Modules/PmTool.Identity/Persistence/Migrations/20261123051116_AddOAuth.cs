using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "grant_id",
                schema: "identity",
                table: "access_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "oauth_clients",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    client_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    redirect_uris = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cache_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oauth_grants",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    client_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    client_host = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    resource = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_grants", x => x.id);
                    table.CheckConstraint("ck_oauth_grants_scopes_not_empty", "cardinality(scopes) >= 1");
                    table.ForeignKey(
                        name: "fk_oauth_grants_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_oauth_grants_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "identity",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "oauth_codes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    redirect_uri = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    code_challenge = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    resource = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_family_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_codes", x => x.id);
                    table.CheckConstraint("ck_oauth_codes_hash_length", "octet_length(code_hash) = 32");
                    table.ForeignKey(
                        name: "fk_oauth_codes_oauth_grants_grant_id",
                        column: x => x.grant_id,
                        principalSchema: "identity",
                        principalTable: "oauth_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_oauth_codes_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "identity",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "oauth_refresh_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_refresh_tokens", x => x.id);
                    table.CheckConstraint("ck_oauth_refresh_tokens_hash_length", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_oauth_refresh_tokens_oauth_grants_grant_id",
                        column: x => x.grant_id,
                        principalSchema: "identity",
                        principalTable: "oauth_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_oauth_refresh_tokens_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "identity",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_tokens_grant_id",
                schema: "identity",
                table: "access_tokens",
                column: "grant_id",
                filter: "grant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_clients_client_id",
                schema: "identity",
                table: "oauth_clients",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oauth_codes_code_hash",
                schema: "identity",
                table: "oauth_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oauth_codes_grant_id",
                schema: "identity",
                table: "oauth_codes",
                column: "grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_codes_organization_id",
                schema: "identity",
                table: "oauth_codes",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_grants_account_id",
                schema: "identity",
                table: "oauth_grants",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ux_oauth_grants_active",
                schema: "identity",
                table: "oauth_grants",
                columns: new[] { "organization_id", "account_id", "client_id", "resource" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_refresh_tokens_family_id",
                schema: "identity",
                table: "oauth_refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_refresh_tokens_grant_id",
                schema: "identity",
                table: "oauth_refresh_tokens",
                column: "grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_refresh_tokens_organization_id",
                schema: "identity",
                table: "oauth_refresh_tokens",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_refresh_tokens_token_hash",
                schema: "identity",
                table: "oauth_refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_access_tokens_o_auth_grants_grant_id",
                schema: "identity",
                table: "access_tokens",
                column: "grant_id",
                principalSchema: "identity",
                principalTable: "oauth_grants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_access_tokens_o_auth_grants_grant_id",
                schema: "identity",
                table: "access_tokens");

            migrationBuilder.DropTable(
                name: "oauth_clients",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "oauth_codes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "oauth_refresh_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "oauth_grants",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_access_tokens_grant_id",
                schema: "identity",
                table: "access_tokens");

            migrationBuilder.DropColumn(
                name: "grant_id",
                schema: "identity",
                table: "access_tokens");
        }
    }
}
