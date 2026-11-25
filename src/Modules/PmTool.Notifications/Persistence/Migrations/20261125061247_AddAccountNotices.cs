using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "project_key",
                schema: "notifications",
                table: "notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<Guid>(
                name: "project_id",
                schema: "notifications",
                table: "notifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "item_title",
                schema: "notifications",
                table: "notifications",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "item_key",
                schema: "notifications",
                table: "notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<Guid>(
                name: "item_id",
                schema: "notifications",
                table: "notifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "account_notice",
                schema: "notifications",
                table: "notifications",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_account_notice",
                schema: "notifications",
                table: "notifications",
                sql: "(kind = 'account_security') = (project_id IS NULL AND item_id IS NULL AND account_notice IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 帳號安全通知沒有項目、專案可以填，欄位改回不可為空之前先刪掉，否則會被填上全零的編號、留下沒有意義的列。
            migrationBuilder.Sql("DELETE FROM notifications.notifications WHERE kind = 'account_security';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_account_notice",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "account_notice",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.AlterColumn<string>(
                name: "project_key",
                schema: "notifications",
                table: "notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "project_id",
                schema: "notifications",
                table: "notifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "item_title",
                schema: "notifications",
                table: "notifications",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "item_key",
                schema: "notifications",
                table: "notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "item_id",
                schema: "notifications",
                table: "notifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
