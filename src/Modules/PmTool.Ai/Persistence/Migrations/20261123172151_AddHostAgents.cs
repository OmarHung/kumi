using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PmTool.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHostAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "host_agents",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    machine_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    machine_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    credential_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pairing_code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pairing_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pairing_started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    server_url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    engines = table.Column<string>(type: "jsonb", nullable: false),
                    logout_requested = table.Column<string>(type: "jsonb", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_agents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "host_job_events",
                schema: "ai",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_job_events", x => new { x.job_id, x.seq });
                });

            migrationBuilder.CreateTable(
                name: "host_jobs",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease = table.Column<int>(type: "integer", nullable: false),
                    last_report_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    cancel_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    finished_by_worker = table.Column<bool>(type: "boolean", nullable: false),
                    finish_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    finish_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    outcome_unknown = table.Column<bool>(type: "boolean", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: true),
                    output_tokens = table.Column<long>(type: "bigint", nullable: true),
                    tool_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tool_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    event_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "host_logins",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engine = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    billing = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_logins", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "host_sources",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engine = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    enabled_models = table.Column<string>(type: "jsonb", nullable: false),
                    default_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    default_effort = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    daily_requests_per_person = table.Column<int>(type: "integer", nullable: true),
                    monthly_requests = table.Column<int>(type: "integer", nullable: true),
                    monthly_tokens = table.Column<long>(type: "bigint", nullable: true),
                    billing_acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    billing_acknowledged_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "host_tool_calls",
                schema: "ai",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    args_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_tool_calls", x => new { x.job_id, x.tool, x.args_hash });
                });

            migrationBuilder.CreateTable(
                name: "host_usage",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_host_usage", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_host_agents_credential_hash",
                schema: "ai",
                table: "host_agents",
                column: "credential_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_host_agents_organization_id_name_key",
                schema: "ai",
                table: "host_agents",
                columns: new[] { "organization_id", "name_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_host_agents_pairing_code_hash",
                schema: "ai",
                table: "host_agents",
                column: "pairing_code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_host_jobs_seq",
                schema: "ai",
                table: "host_jobs",
                column: "seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_host_jobs_source_id_state_seq",
                schema: "ai",
                table: "host_jobs",
                columns: new[] { "source_id", "state", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_host_jobs_tool_token_hash",
                schema: "ai",
                table: "host_jobs",
                column: "tool_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_host_logins_agent_id_engine_state",
                schema: "ai",
                table: "host_logins",
                columns: new[] { "agent_id", "engine", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_host_logins_code_hash",
                schema: "ai",
                table: "host_logins",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_host_sources_agent_id_engine",
                schema: "ai",
                table: "host_sources",
                columns: new[] { "agent_id", "engine" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_host_sources_organization_id_name_key",
                schema: "ai",
                table: "host_sources",
                columns: new[] { "organization_id", "name_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_host_usage_organization_id_source_id_created_at",
                schema: "ai",
                table: "host_usage",
                columns: new[] { "organization_id", "source_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_host_usage_organization_id_source_id_person_id_created_at",
                schema: "ai",
                table: "host_usage",
                columns: new[] { "organization_id", "source_id", "person_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "host_agents",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "host_job_events",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "host_jobs",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "host_logins",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "host_sources",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "host_tool_calls",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "host_usage",
                schema: "ai");
        }
    }
}
