using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reply_to_id",
                schema: "work",
                table: "item_activities",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_activities_item_id_reply_to_id",
                schema: "work",
                table: "item_activities",
                columns: new[] { "item_id", "reply_to_id" },
                filter: "reply_to_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_item_activities_reply_to_id",
                schema: "work",
                table: "item_activities",
                column: "reply_to_id");

            migrationBuilder.AddForeignKey(
                name: "fk_item_activities_item_activities_reply_to_id",
                schema: "work",
                table: "item_activities",
                column: "reply_to_id",
                principalSchema: "work",
                principalTable: "item_activities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_item_activities_item_activities_reply_to_id",
                schema: "work",
                table: "item_activities");

            migrationBuilder.DropIndex(
                name: "ix_item_activities_item_id_reply_to_id",
                schema: "work",
                table: "item_activities");

            migrationBuilder.DropIndex(
                name: "ix_item_activities_reply_to_id",
                schema: "work",
                table: "item_activities");

            migrationBuilder.DropColumn(
                name: "reply_to_id",
                schema: "work",
                table: "item_activities");
        }
    }
}
