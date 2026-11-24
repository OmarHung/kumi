using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_agent_events",
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
                    table.PrimaryKey("pk_local_agent_events", x => new { x.job_id, x.seq });
                });

            migrationBuilder.CreateTable(
                name: "local_agent_jobs",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engine_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_report_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    event_count = table.Column<int>(type: "integer", nullable: false),
                    finish_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    finish_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: true),
                    output_tokens = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_agent_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "local_agents",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    machine_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    agent_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_agents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "local_engines",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    models = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_local_engines", x => x.id);
                    table.ForeignKey(
                        name: "fk_local_engines_local_agents_agent_id",
                        column: x => x.agent_id,
                        principalSchema: "ai",
                        principalTable: "local_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_local_agent_jobs_organization_id_person_id_state_created_at",
                schema: "ai",
                table: "local_agent_jobs",
                columns: new[] { "organization_id", "person_id", "state", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_local_agents_organization_id_person_id_machine_key",
                schema: "ai",
                table: "local_agents",
                columns: new[] { "organization_id", "person_id", "machine_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_local_engines_agent_id_kind",
                schema: "ai",
                table: "local_engines",
                columns: new[] { "agent_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_agent_events",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "local_agent_jobs",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "local_engines",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "local_agents",
                schema: "ai");
        }
    }
}
