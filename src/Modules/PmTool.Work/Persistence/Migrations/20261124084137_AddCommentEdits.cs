using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentEdits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "work",
                table: "item_activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by",
                schema: "work",
                table: "item_activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "edited_at",
                schema: "work",
                table: "item_activities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "comment_revisions",
                schema: "work",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_comment_revisions_item_activities_comment_id",
                        column: x => x.comment_id,
                        principalSchema: "work",
                        principalTable: "item_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_comment_revisions_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "work",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comment_revisions_comment_id_number",
                schema: "work",
                table: "comment_revisions",
                columns: new[] { "comment_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comment_revisions_item_id",
                schema: "work",
                table: "comment_revisions",
                column: "item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comment_revisions",
                schema: "work");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "work",
                table: "item_activities");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                schema: "work",
                table: "item_activities");

            migrationBuilder.DropColumn(
                name: "edited_at",
                schema: "work",
                table: "item_activities");
        }
    }
}
