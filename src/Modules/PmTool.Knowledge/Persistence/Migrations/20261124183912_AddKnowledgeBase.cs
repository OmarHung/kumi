using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Knowledge.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "knowledge");

            migrationBuilder.CreateTable(
                name: "spaces",
                schema: "knowledge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    next_page_number = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pages",
                schema: "knowledge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    is_restricted = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deletion_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pages", x => x.id);
                    table.ForeignKey(
                        name: "fk_pages_pages_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "knowledge",
                        principalTable: "pages",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_pages_spaces_space_id",
                        column: x => x.space_id,
                        principalSchema: "knowledge",
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "page_attachments",
                schema: "knowledge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_page_attachments_pages_page_id",
                        column: x => x.page_id,
                        principalSchema: "knowledge",
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "page_restrictions",
                schema: "knowledge",
                columns: table => new
                {
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_restrictions", x => new { x.page_id, x.person_id });
                    table.ForeignKey(
                        name: "fk_page_restrictions_pages_page_id",
                        column: x => x.page_id,
                        principalSchema: "knowledge",
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "page_versions",
                schema: "knowledge",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    restored_from = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_page_versions_pages_page_id",
                        column: x => x.page_id,
                        principalSchema: "knowledge",
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_page_attachments_page_id_uploaded_at",
                schema: "knowledge",
                table: "page_attachments",
                columns: new[] { "page_id", "uploaded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_page_versions_page_id_number",
                schema: "knowledge",
                table: "page_versions",
                columns: new[] { "page_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pages_parent_id",
                schema: "knowledge",
                table: "pages",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_pages_space_id_deletion_id",
                schema: "knowledge",
                table: "pages",
                columns: new[] { "space_id", "deletion_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pages_space_id_number",
                schema: "knowledge",
                table: "pages",
                columns: new[] { "space_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pages_space_id_parent_id_position",
                schema: "knowledge",
                table: "pages",
                columns: new[] { "space_id", "parent_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_spaces_organization_id_key",
                schema: "knowledge",
                table: "spaces",
                columns: new[] { "organization_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_spaces_organization_project",
                schema: "knowledge",
                table: "spaces",
                columns: new[] { "organization_id", "project_id" },
                unique: true,
                filter: "project_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page_attachments",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "page_restrictions",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "page_versions",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "pages",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "spaces",
                schema: "knowledge");
        }
    }
}
