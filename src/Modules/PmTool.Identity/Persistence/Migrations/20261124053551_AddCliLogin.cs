using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCliLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "machine_name",
                schema: "identity",
                table: "access_tokens",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cli_login_codes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    code_challenge = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    machine_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    cli_scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    include_agent = table.Column<bool>(type: "boolean", nullable: false),
                    agent_skipped = table.Column<bool>(type: "boolean", nullable: false),
                    expires_in_days = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cli_login_codes", x => x.id);
                    table.CheckConstraint("ck_cli_login_codes_has_token", "cardinality(cli_scopes) >= 1 OR include_agent");
                    table.CheckConstraint("ck_cli_login_codes_hash_length", "octet_length(code_hash) = 32");
                    table.ForeignKey(
                        name: "fk_cli_login_codes_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cli_login_codes_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "identity",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cli_login_codes_account_id",
                schema: "identity",
                table: "cli_login_codes",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_cli_login_codes_code_hash",
                schema: "identity",
                table: "cli_login_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cli_login_codes_organization_id_expires_at",
                schema: "identity",
                table: "cli_login_codes",
                columns: new[] { "organization_id", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cli_login_codes",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "machine_name",
                schema: "identity",
                table: "access_tokens");
        }
    }
}
