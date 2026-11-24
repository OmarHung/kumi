using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemArchiveAndTypeChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_items_organization_id_assignee_id_state_category",
                schema: "work",
                table: "items");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "work",
                table: "items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by",
                schema: "work",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "change_set_id",
                schema: "work",
                table: "archived_field_values",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "item_activity_id",
                schema: "work",
                table: "archived_field_values",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_organization_id_assignee_id_state_category",
                schema: "work",
                table: "items",
                columns: new[] { "organization_id", "assignee_id", "state_category" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_archived_field_values_item_activity_id",
                schema: "work",
                table: "archived_field_values",
                column: "item_activity_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_archived_field_values_one_source",
                schema: "work",
                table: "archived_field_values",
                sql: "(change_set_id IS NULL) <> (item_activity_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_archived_field_values_item_activities_item_activity_id",
                schema: "work",
                table: "archived_field_values",
                column: "item_activity_id",
                principalSchema: "work",
                principalTable: "item_activities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 舊的資料表沒有 deleted_at，也只記得住變更集封存的值。有刪除（封存）的項目或改類型封存的值時，
            // 降版會讓刪除的項目重新出現、或得把那些值丟掉，兩者都不能默默發生，所以整個擋下；交易退回，什麼都沒改。
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                  deleted_items bigint := (SELECT count(*) FROM work.items WHERE deleted_at IS NOT NULL);
                  type_change_values bigint := (SELECT count(*) FROM work.archived_field_values WHERE change_set_id IS NULL);
                BEGIN
                  IF deleted_items > 0 THEN
                    RAISE EXCEPTION '不能降版到 ChangeSets：有 % 個刪除（封存）的項目，舊的資料表沒有 deleted_at，降版後它們會重新出現。', deleted_items;
                  END IF;
                  IF type_change_values > 0 THEN
                    RAISE EXCEPTION '不能降版到 ChangeSets：有 % 筆改類型時封存的欄位值，舊的封存表只記得住變更集封存的值，降版會把它們丟掉。', type_change_values;
                  END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_archived_field_values_item_activities_item_activity_id",
                schema: "work",
                table: "archived_field_values");

            migrationBuilder.DropIndex(
                name: "ix_items_organization_id_assignee_id_state_category",
                schema: "work",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_archived_field_values_item_activity_id",
                schema: "work",
                table: "archived_field_values");

            migrationBuilder.DropCheckConstraint(
                name: "ck_archived_field_values_one_source",
                schema: "work",
                table: "archived_field_values");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "work",
                table: "items");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                schema: "work",
                table: "items");

            migrationBuilder.DropColumn(
                name: "item_activity_id",
                schema: "work",
                table: "archived_field_values");

            migrationBuilder.AlterColumn<Guid>(
                name: "change_set_id",
                schema: "work",
                table: "archived_field_values",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_organization_id_assignee_id_state_category",
                schema: "work",
                table: "items",
                columns: new[] { "organization_id", "assignee_id", "state_category" });
        }
    }
}
