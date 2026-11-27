using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_reads",
                schema: "ai",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_reads", x => new { x.conversation_id, x.project_id });
                    table.ForeignKey(
                        name: "fk_conversation_reads_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "ai",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_reads_organization_id_person_id",
                schema: "ai",
                table: "conversation_reads",
                columns: new[] { "organization_id", "person_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_reads",
                schema: "ai");
        }
    }
}
