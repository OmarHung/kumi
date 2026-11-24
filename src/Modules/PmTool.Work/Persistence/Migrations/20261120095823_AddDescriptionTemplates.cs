using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDescriptionTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "description_templates",
                schema: "work",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_description_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_description_templates_organization_id",
                schema: "work",
                table: "description_templates",
                column: "organization_id");

            // 名稱不分大小寫唯一、每個組織最多一份預設：EF 的模型描述不了運算式與部分索引，所以用 SQL 建；
            // 寫入先在交易裡排隊檢查，這兩個是同時寫入時的最後一道防線。
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_description_templates_organization_lower_name ON work.description_templates (organization_id, lower(name));");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_description_templates_organization_default ON work.description_templates (organization_id) WHERE is_default;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "description_templates",
                schema: "work");
        }
    }
}
