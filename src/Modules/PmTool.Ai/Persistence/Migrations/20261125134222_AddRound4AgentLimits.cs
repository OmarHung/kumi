using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRound4AgentLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "daily_requests_per_person",
                schema: "ai",
                table: "settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "monthly_requests",
                schema: "ai",
                table: "settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "monthly_tokens",
                schema: "ai",
                table: "settings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "proposals",
                schema: "ai",
                table: "local_agent_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "tool_calls",
                schema: "ai",
                table: "local_agent_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "proposals",
                schema: "ai",
                table: "host_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "tool_calls",
                schema: "ai",
                table: "host_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "credential_expires_at",
                schema: "ai",
                table: "host_agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_credential_expires_at",
                schema: "ai",
                table: "host_agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "next_credential_hash",
                schema: "ai",
                table: "host_agents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspect_machine_key",
                schema: "ai",
                table: "host_agents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspect_machine_name",
                schema: "ai",
                table: "host_agents",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suspect_seen_at",
                schema: "ai",
                table: "host_agents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "saw_all_items",
                schema: "ai",
                table: "conversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 既有的組織從預設的每人每天上限開始（null 是不限，只有管理員明講才是）。
            migrationBuilder.Sql("UPDATE ai.settings SET daily_requests_per_person = 200;");

            // 既有的機器憑證從現在起算期限；worker 在線時會在到期前自己換新。
            migrationBuilder.Sql(
                "UPDATE ai.host_agents SET credential_expires_at = now() + interval '90 days' WHERE credential_hash IS NOT NULL;");

            // 既有的對話不知道當時看得到多少：歷史裡讀過項目的（用過 get_item、search_items）當作看得到全部，
            // 之後這個人只剩自己的項目時整則刪掉；沒讀過項目的不受影響。
            migrationBuilder.Sql(
                "UPDATE ai.conversations c SET saw_all_items = true WHERE EXISTS (SELECT 1 FROM ai.messages m "
                + "WHERE m.conversation_id = c.id AND (m.transcript::text LIKE '%get_item%' OR m.transcript::text LIKE '%search_items%' "
                + "OR m.parts::text LIKE '%get_item%' OR m.parts::text LIKE '%search_items%'));");

            migrationBuilder.CreateTable(
                name: "connection_usage",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    finish_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connection_usage", x => x.id);
                });

            // 既有的組織連線回覆補進用量表：這個月的上限與連線的用量從同一份紀錄算（專案代號當時沒記，留空）。
            migrationBuilder.Sql(
                "INSERT INTO ai.connection_usage (id, organization_id, connection_id, person_id, project_key, state, finish_status, "
                + "input_tokens, output_tokens, created_at, settled_at) "
                + "SELECT m.id, m.organization_id, m.connection_id, c.person_id, '', 'settled', m.status, "
                + "coalesce(m.input_tokens, 0), coalesce(m.output_tokens, 0), m.created_at, m.created_at "
                + "FROM ai.messages m JOIN ai.conversations c ON c.id = m.conversation_id "
                + "JOIN ai.connections k ON k.id = m.connection_id AND k.owner_person_id IS NULL "
                + "WHERE m.role = 'assistant';");

            migrationBuilder.CreateIndex(
                name: "ix_host_agents_next_credential_hash",
                schema: "ai",
                table: "host_agents",
                column: "next_credential_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_updated_at",
                schema: "ai",
                table: "conversations",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_connection_usage_organization_id_created_at",
                schema: "ai",
                table: "connection_usage",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_connection_usage_organization_id_person_id_created_at",
                schema: "ai",
                table: "connection_usage",
                columns: new[] { "organization_id", "person_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connection_usage",
                schema: "ai");

            migrationBuilder.DropIndex(
                name: "ix_host_agents_next_credential_hash",
                schema: "ai",
                table: "host_agents");

            migrationBuilder.DropIndex(
                name: "ix_conversations_updated_at",
                schema: "ai",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "daily_requests_per_person",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "monthly_requests",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "monthly_tokens",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "proposals",
                schema: "ai",
                table: "local_agent_jobs");

            migrationBuilder.DropColumn(
                name: "tool_calls",
                schema: "ai",
                table: "local_agent_jobs");

            migrationBuilder.DropColumn(
                name: "proposals",
                schema: "ai",
                table: "host_jobs");

            migrationBuilder.DropColumn(
                name: "tool_calls",
                schema: "ai",
                table: "host_jobs");

            migrationBuilder.DropColumn(
                name: "credential_expires_at",
                schema: "ai",
                table: "host_agents");

            migrationBuilder.DropColumn(
                name: "next_credential_expires_at",
                schema: "ai",
                table: "host_agents");

            migrationBuilder.DropColumn(
                name: "next_credential_hash",
                schema: "ai",
                table: "host_agents");

            migrationBuilder.DropColumn(
                name: "suspect_machine_key",
                schema: "ai",
                table: "host_agents");

            migrationBuilder.DropColumn(
                name: "suspect_machine_name",
                schema: "ai",
                table: "host_agents");

            migrationBuilder.DropColumn(
                name: "suspect_seen_at",
                schema: "ai",
                table: "host_agents");

            migrationBuilder.DropColumn(
                name: "saw_all_items",
                schema: "ai",
                table: "conversations");
        }
    }
}
