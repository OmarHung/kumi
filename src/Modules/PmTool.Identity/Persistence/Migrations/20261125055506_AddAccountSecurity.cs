using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSecurity : Migration
    {
        private const string NormalizedEmailCollisionCheck = """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM identity.accounts
                    GROUP BY lower(normalize(email, NFKC))
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'identity.accounts 有 Email 在改成 NFKC 加小寫之後會重複，遷移沒有改任何資料。請先用 SQL 人工修改其中一個帳號的 Email，再重新部署。';
                END IF;
            END
            $$;
            """;

        private const string NormalizeEmails = """
            UPDATE identity.accounts
            SET email = lower(normalize(email, NFKC))
            WHERE email <> lower(normalize(email, NFKC));
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Email 的唯一性靠存下來的字串本身：改成 NFKC 加小寫之後，原本不同的兩個帳號可能變成同一個。
            // 先檢查，會撞就整個遷移失敗、什麼都不改；訊息不帶 Email，免得進了日誌。
            migrationBuilder.Sql(NormalizedEmailCollisionCheck);
            migrationBuilder.Sql(NormalizeEmails);

            migrationBuilder.AddColumn<Guid[]>(
                name: "issued_token_ids",
                schema: "identity",
                table: "cli_login_codes",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "created_from",
                schema: "identity",
                table: "access_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_events_outcome", "outcome IN ('succeeded', 'failed')");
                    table.ForeignKey(
                        name: "fk_audit_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "identity",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_oauth_clients_kind_created_at",
                schema: "identity",
                table: "oauth_clients",
                columns: new[] { "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_organization_id_actor_person_id_occurred_at",
                schema: "identity",
                table: "audit_events",
                columns: new[] { "organization_id", "actor_person_id", "occurred_at" },
                descending: new[] { false, false, true },
                filter: "actor_person_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_organization_id_occurred_at_id",
                schema: "identity",
                table: "audit_events",
                columns: new[] { "organization_id", "occurred_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_organization_id_target_person_id_occurred_at",
                schema: "identity",
                table: "audit_events",
                columns: new[] { "organization_id", "target_person_id", "occurred_at" },
                descending: new[] { false, false, true },
                filter: "target_person_id IS NOT NULL");
        }

        // Email 的改寫不還原：原本的寫法已經找不回來，而正規化之後的 Email 對舊版程式照樣是合法的。
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "ix_oauth_clients_kind_created_at",
                schema: "identity",
                table: "oauth_clients");

            migrationBuilder.DropColumn(
                name: "issued_token_ids",
                schema: "identity",
                table: "cli_login_codes");

            migrationBuilder.DropColumn(
                name: "created_from",
                schema: "identity",
                table: "access_tokens");
        }
    }
}
