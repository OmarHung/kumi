using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentReplayAndSweepMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "replay_skipped_at",
                schema: "ai",
                table: "attachments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sweep_failed_at",
                schema: "ai",
                table: "attachments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "replay_skipped_at",
                schema: "ai",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "sweep_failed_at",
                schema: "ai",
                table: "attachments");
        }
    }
}
