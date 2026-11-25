using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 既有帳號的 Email 改成 NFKC 加小寫，不在這裡用 SQL 做：資料庫的 lower 和 .NET 對少數字元（例如帶點的大寫 I）結果不同，
            // 改出來的值本人用原本的寫法登入就比對不到。改寫由 IdentityMigrator 在遷移之後用登入同一套規則做。

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
