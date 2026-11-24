using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attachments",
                schema: "work",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_attachments_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "work",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_item_id",
                schema: "work",
                table: "attachments",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_organization_id_item_id_uploaded_at",
                schema: "work",
                table: "attachments",
                columns: new[] { "organization_id", "item_id", "uploaded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 舊的資料庫沒有 attachments：降版會讓檔案變成沒人參考的孤兒（還在儲存空間裡，但項目上看不到）。有附件時整個擋下，交易退回。
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                  attachments bigint := (SELECT count(*) FROM work.attachments);
                BEGIN
                  IF attachments > 0 THEN
                    RAISE EXCEPTION '不能降版到 AddAttachments 之前：有 % 個附件，舊的資料庫記不住它們，降版後檔案會變成沒人參考的孤兒。請先刪除附件。', attachments;
                  END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "attachments",
                schema: "work");
        }
    }
}
