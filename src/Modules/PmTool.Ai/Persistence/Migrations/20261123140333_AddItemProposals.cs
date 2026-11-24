using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItemProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "item_key",
                schema: "ai",
                table: "messages",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_proposals",
                schema: "ai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    item_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    item_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    description_revision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_proposals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_proposals_organization_id_person_id_created_at",
                schema: "ai",
                table: "item_proposals",
                columns: new[] { "organization_id", "person_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_proposals",
                schema: "ai");

            migrationBuilder.DropColumn(
                name: "item_key",
                schema: "ai",
                table: "messages");
        }
    }
}
