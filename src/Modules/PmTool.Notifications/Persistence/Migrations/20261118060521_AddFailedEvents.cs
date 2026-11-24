using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFailedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "failed_events",
                schema: "notifications",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    error_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_failed_events", x => x.event_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_failed_events_organization_id_last_failed_at",
                schema: "notifications",
                table: "failed_events",
                columns: new[] { "organization_id", "last_failed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "failed_events",
                schema: "notifications");
        }
    }
}
