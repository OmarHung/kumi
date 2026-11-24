using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    display_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_tokens", x => x.id);
                    table.CheckConstraint("ck_access_tokens_scopes_not_empty", "cardinality(scopes) >= 1");
                    table.CheckConstraint("ck_access_tokens_token_hash_length", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_access_tokens_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_tokens_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "identity",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_tokens_account_id",
                schema: "identity",
                table: "access_tokens",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_tokens_organization_id_account_id",
                schema: "identity",
                table: "access_tokens",
                columns: new[] { "organization_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_access_tokens_token_hash",
                schema: "identity",
                table: "access_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_tokens",
                schema: "identity");
        }
    }
}
