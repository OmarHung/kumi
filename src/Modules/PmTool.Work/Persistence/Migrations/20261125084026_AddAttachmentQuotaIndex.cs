using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Work.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentQuotaIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_attachments_quota",
                schema: "work",
                table: "attachments",
                columns: new[] { "organization_id", "uploaded_by", "uploaded_at" })
                .Annotation("Npgsql:IndexInclude", new[] { "size_bytes" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_attachments_quota",
                schema: "work",
                table: "attachments");
        }
    }
}
