using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMyWorkSavedViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "project_id",
                schema: "work",
                table: "saved_views",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // 「我的工作」的檢視沒有專案，專案的唯一索引管不到它（null 彼此不相等），名稱不分大小寫唯一要另外一個；
            // EF 的模型描述不了運算式索引，所以用 SQL 建。舊資料都有專案，不受影響。
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_saved_views_my_work_owner_lower_name ON work.saved_views (organization_id, owner_id, lower(name)) WHERE project_id IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 舊的資料表每個檢視都要有專案；「我的工作」的檢視降版後沒有地方放。有的話整個擋下，交易退回，什麼都沒改。
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                  my_work_views bigint := (SELECT count(*) FROM work.saved_views WHERE project_id IS NULL);
                BEGIN
                  IF my_work_views > 0 THEN
                    RAISE EXCEPTION '不能降版到 AddMyWorkSavedViews 之前：有 % 個「我的工作」的檢視，舊的資料表每個檢視都要有專案，降版會弄丟它們。請先刪掉。', my_work_views;
                  END IF;
                END $$;
                """);

            migrationBuilder.Sql("DROP INDEX work.ux_saved_views_my_work_owner_lower_name;");

            migrationBuilder.AlterColumn<Guid>(
                name: "project_id",
                schema: "work",
                table: "saved_views",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
