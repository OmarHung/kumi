using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_handled_at",
                schema: "notifications",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            // 加這個欄位之前的通知一律當作處理完了：開了寄信之後不會把舊通知補寄出去。
            migrationBuilder.Sql("UPDATE notifications.notifications SET email_handled_at = created_at;");

            migrationBuilder.CreateTable(
                name: "email_preferences",
                schema: "notifications",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_preferences", x => new { x.organization_id, x.person_id });
                });

            migrationBuilder.CreateTable(
                name: "email_settings",
                schema: "notifications",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    port = table.Column<int>(type: "integer", nullable: true),
                    security = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    username = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    protected_password = table.Column<string>(type: "text", nullable: true),
                    from_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    from_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    site_url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    last_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    retry_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_settings", x => x.organization_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_email_pending",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "organization_id", "created_at" },
                filter: "email_handled_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_preferences",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "email_settings",
                schema: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_email_pending",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "email_handled_at",
                schema: "notifications",
                table: "notifications");
        }
    }
}
