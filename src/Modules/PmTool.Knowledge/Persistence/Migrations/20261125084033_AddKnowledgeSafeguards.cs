using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Knowledge.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeSafeguards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 預先檢查放在最前面：既有的資料裡有上層在別的空間的頁面時，換成複合外鍵會失敗；
            // 在這裡就讓整個遷移失敗，而不是做到一半。遷移在同一個交易裡，失敗時什麼都不會改。
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM knowledge.pages c JOIN knowledge.pages p ON p.id = c.parent_id WHERE p.space_id <> c.space_id) THEN
                    RAISE EXCEPTION 'knowledge.pages 有上層在別的空間的頁面，請先修正資料再遷移（上層與子頁必須在同一個空間）。';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_pages_pages_parent_id",
                schema: "knowledge",
                table: "pages");

            migrationBuilder.DropIndex(
                name: "ix_pages_parent_id",
                schema: "knowledge",
                table: "pages");

            migrationBuilder.AddColumn<Guid>(
                name: "restricted_by",
                schema: "knowledge",
                table: "pages",
                type: "uuid",
                nullable: true);

            // 舊資料的「誰設的限制」：建立者還在這一頁自己的名單裡就算建立者設的，否則留空（只有空間的管理者能放寬）。
            migrationBuilder.Sql(
                """
                UPDATE knowledge.pages p
                   SET restricted_by = p.created_by
                 WHERE p.is_restricted
                   AND EXISTS (SELECT 1 FROM knowledge.page_restrictions r
                                WHERE r.organization_id = p.organization_id AND r.page_id = p.id AND r.person_id = p.created_by);
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_pages_space_id_id",
                schema: "knowledge",
                table: "pages",
                columns: new[] { "space_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_pages_trash",
                schema: "knowledge",
                table: "pages",
                columns: new[] { "space_id", "deleted_at" },
                descending: new[] { false, true },
                filter: "deletion_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_page_restrictions_person",
                schema: "knowledge",
                table: "page_restrictions",
                columns: new[] { "organization_id", "person_id", "page_id" });

            migrationBuilder.CreateIndex(
                name: "ix_page_attachments_quota",
                schema: "knowledge",
                table: "page_attachments",
                columns: new[] { "organization_id", "uploaded_by", "uploaded_at" })
                .Annotation("Npgsql:IndexInclude", new[] { "size_bytes" });

            migrationBuilder.AddForeignKey(
                name: "fk_pages_pages_space_id_parent_id",
                schema: "knowledge",
                table: "pages",
                columns: new[] { "space_id", "parent_id" },
                principalSchema: "knowledge",
                principalTable: "pages",
                principalColumns: new[] { "space_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pages_pages_space_id_parent_id",
                schema: "knowledge",
                table: "pages");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_pages_space_id_id",
                schema: "knowledge",
                table: "pages");

            migrationBuilder.DropIndex(
                name: "ix_pages_trash",
                schema: "knowledge",
                table: "pages");

            migrationBuilder.DropIndex(
                name: "ix_page_restrictions_person",
                schema: "knowledge",
                table: "page_restrictions");

            migrationBuilder.DropIndex(
                name: "ix_page_attachments_quota",
                schema: "knowledge",
                table: "page_attachments");

            migrationBuilder.DropColumn(
                name: "restricted_by",
                schema: "knowledge",
                table: "pages");

            migrationBuilder.CreateIndex(
                name: "ix_pages_parent_id",
                schema: "knowledge",
                table: "pages",
                column: "parent_id");

            migrationBuilder.AddForeignKey(
                name: "fk_pages_pages_parent_id",
                schema: "knowledge",
                table: "pages",
                column: "parent_id",
                principalSchema: "knowledge",
                principalTable: "pages",
                principalColumn: "id");
        }
    }
}
