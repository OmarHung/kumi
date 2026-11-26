using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "add_members",
                schema: "ai",
                table: "new_item_proposals",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.CreateTable(
                name: "member_proposals",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_proposals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_member_proposals_organization_id_person_id",
                schema: "ai",
                table: "member_proposals",
                columns: new[] { "organization_id", "person_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "member_proposals",
                schema: "ai");

            migrationBuilder.DropColumn(
                name: "add_members",
                schema: "ai",
                table: "new_item_proposals");
        }
    }
}
