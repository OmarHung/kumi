using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "saved_views",
                schema: "work",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    layout = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    query = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_views", x => x.id);
                    table.ForeignKey(
                        name: "fk_saved_views_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "work",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_saved_views_organization_id_project_id_owner_id",
                schema: "work",
                table: "saved_views",
                columns: new[] { "organization_id", "project_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_saved_views_project_id",
                schema: "work",
                table: "saved_views",
                column: "project_id");

            // 名稱不分大小寫唯一：EF 的模型描述不了運算式索引，所以用 SQL 建；同一個人同時存同名的檢視，只有一個成功。
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_saved_views_project_owner_lower_name ON work.saved_views (project_id, owner_id, lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saved_views",
                schema: "work");
        }
    }
}
