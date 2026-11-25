using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSafeguards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "excerpt_edited_at",
                schema: "notifications",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            // 較早的通知沒有留言編號：2026-11-18 之前的事件沒有 activity_id，沒辦法從事件對到留言，
            // 只能用（項目、做的人、時刻）對到 work.item_activities 的那一則留言（通知的建立時間就是事件發生的時刻，
            // 和留言的 at 是同一個時刻）。對不上的（例如說明裡的提及）照舊是 null。
            // 這是通知模組唯一一處讀工作引擎資料表的地方，是模組邊界的一次性例外：跑一次就好，
            // 不必每次啟動掃一遍，也不必為這件事在工作引擎加一個永遠留著的合約。遷移用資料表擁有者的連線，讀得到 work。
            // to_regclass 的保護讓只有通知 schema 的資料庫（例如測試）照樣跑得過。Down 不還原：分不出哪些是這次補的。
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                  IF to_regclass('work.item_activities') IS NOT NULL
                     AND EXISTS (SELECT 1 FROM notifications.notifications
                                  WHERE comment_id IS NULL AND kind IN ('commented', 'mentioned', 'replied') AND item_id IS NOT NULL) THEN
                    UPDATE notifications.notifications n
                       SET comment_id = a.id
                      FROM work.item_activities a
                     WHERE n.comment_id IS NULL
                       AND n.kind IN ('commented', 'mentioned', 'replied')
                       AND a.organization_id = n.organization_id AND a.item_id = n.item_id
                       AND a.actor_id = n.actor_person_id AND a.kind = 'commented' AND a.at = n.created_at;
                  END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_comment",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "organization_id", "comment_id", "recipient_id" },
                filter: "comment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_item",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "organization_id", "item_id" },
                filter: "item_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_comment",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_item",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "excerpt_edited_at",
                schema: "notifications",
                table: "notifications");
        }
    }
}
