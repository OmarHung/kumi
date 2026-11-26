using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "job_title",
                schema: "identity",
                table: "people",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "responsibilities",
                schema: "identity",
                table: "people",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "job_title",
                schema: "identity",
                table: "people");

            migrationBuilder.DropColumn(
                name: "responsibilities",
                schema: "identity",
                table: "people");
        }
    }
}
