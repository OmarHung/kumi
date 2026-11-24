using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCyclesAndMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cycle_id",
                schema: "work",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "milestone_id",
                schema: "work",
                table: "items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cycles",
                schema: "work",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycles", x => x.id);
                    table.ForeignKey(
                        name: "fk_cycles_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "work",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "milestones",
                schema: "work",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_milestones_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "work",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_items_cycle_id",
                schema: "work",
                table: "items",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_milestone_id",
                schema: "work",
                table: "items",
                column: "milestone_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_organization_id_project_id_cycle_id",
                schema: "work",
                table: "items",
                columns: new[] { "organization_id", "project_id", "cycle_id" },
                filter: "deleted_at IS NULL AND cycle_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_items_organization_id_project_id_milestone_id",
                schema: "work",
                table: "items",
                columns: new[] { "organization_id", "project_id", "milestone_id" },
                filter: "deleted_at IS NULL AND milestone_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cycles_organization_id_project_id_start_date",
                schema: "work",
                table: "cycles",
                columns: new[] { "organization_id", "project_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_cycles_project_id",
                schema: "work",
                table: "cycles",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_milestones_organization_id_project_id_due_date",
                schema: "work",
                table: "milestones",
                columns: new[] { "organization_id", "project_id", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_milestones_project_id",
                schema: "work",
                table: "milestones",
                column: "project_id");

            migrationBuilder.AddForeignKey(
                name: "fk_items_cycles_cycle_id",
                schema: "work",
                table: "items",
                column: "cycle_id",
                principalSchema: "work",
                principalTable: "cycles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_items_milestones_milestone_id",
                schema: "work",
                table: "items",
                column: "milestone_id",
                principalSchema: "work",
                principalTable: "milestones",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // 名稱不分大小寫唯一：EF 的模型描述不了運算式索引，所以用 SQL 建；同時建立同名的只有一個成功。
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_cycles_project_lower_name ON work.cycles (project_id, lower(name));");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_milestones_project_lower_name ON work.milestones (project_id, lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_items_cycles_cycle_id",
                schema: "work",
                table: "items");

            migrationBuilder.DropForeignKey(
                name: "fk_items_milestones_milestone_id",
                schema: "work",
                table: "items");

            migrationBuilder.DropTable(
                name: "cycles",
                schema: "work");

            migrationBuilder.DropTable(
                name: "milestones",
                schema: "work");

            migrationBuilder.DropIndex(
                name: "ix_items_cycle_id",
                schema: "work",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_milestone_id",
                schema: "work",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_organization_id_project_id_cycle_id",
                schema: "work",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_items_organization_id_project_id_milestone_id",
                schema: "work",
                table: "items");

            migrationBuilder.DropColumn(
                name: "cycle_id",
                schema: "work",
                table: "items");

            migrationBuilder.DropColumn(
                name: "milestone_id",
                schema: "work",
                table: "items");
        }
    }
}
