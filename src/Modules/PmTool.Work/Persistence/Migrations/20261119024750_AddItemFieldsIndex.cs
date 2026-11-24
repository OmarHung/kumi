using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItemFieldsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_items_fields",
                schema: "work",
                table: "items",
                column: "fields",
                filter: "deleted_at IS NULL")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_items_fields",
                schema: "work",
                table: "items");
        }
    }
}
