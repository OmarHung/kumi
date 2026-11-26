using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentJobAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "attachment_ids",
                schema: "ai",
                table: "local_agent_jobs",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<Guid[]>(
                name: "attachment_ids",
                schema: "ai",
                table: "host_jobs",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachment_ids",
                schema: "ai",
                table: "local_agent_jobs");

            migrationBuilder.DropColumn(
                name: "attachment_ids",
                schema: "ai",
                table: "host_jobs");
        }
    }
}
