using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_change_set_number",
                schema: "work",
                table: "projects",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "changesets",
                schema: "work",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    base_version = table.Column<long>(type: "bigint", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    proposed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    applied_version = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_changesets", x => x.id);
                    table.ForeignKey(
                        name: "fk_changesets_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "work",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archived_field_values",
                schema: "work",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    change_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archived_field_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_archived_field_values_changesets_change_set_id",
                        column: x => x.change_set_id,
                        principalSchema: "work",
                        principalTable: "changesets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_archived_field_values_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "work",
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_archived_field_values_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "work",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archived_field_values_change_set_id",
                schema: "work",
                table: "archived_field_values",
                column: "change_set_id");

            migrationBuilder.CreateIndex(
                name: "ix_archived_field_values_item_id_field",
                schema: "work",
                table: "archived_field_values",
                columns: new[] { "item_id", "field" });

            migrationBuilder.CreateIndex(
                name: "ix_archived_field_values_project_id",
                schema: "work",
                table: "archived_field_values",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_changesets_project_id_number",
                schema: "work",
                table: "changesets",
                columns: new[] { "project_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archived_field_values",
                schema: "work");

            migrationBuilder.DropTable(
                name: "changesets",
                schema: "work");

            migrationBuilder.DropColumn(
                name: "last_change_set_number",
                schema: "work",
                table: "projects");
        }
    }
}
