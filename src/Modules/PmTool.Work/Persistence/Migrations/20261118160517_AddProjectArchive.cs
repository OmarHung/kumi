using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "work",
                table: "projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "archived_by",
                schema: "work",
                table: "projects",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 舊的資料表沒有 archived_at，降版會讓封存的專案默默變回可以寫入。有封存的專案時整個擋下，交易退回，什麼都沒改。
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                  archived_projects bigint := (SELECT count(*) FROM work.projects WHERE archived_at IS NOT NULL);
                BEGIN
                  IF archived_projects > 0 THEN
                    RAISE EXCEPTION '不能降版到 AddProjectArchive 之前：有 % 個封存的專案，舊的資料表沒有 archived_at，降版後它們會變回可以寫入。請先取消封存。', archived_projects;
                  END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "work",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "archived_by",
                schema: "work",
                table: "projects");
        }
    }
}
