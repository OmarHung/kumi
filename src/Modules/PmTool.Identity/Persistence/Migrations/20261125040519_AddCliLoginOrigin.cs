using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCliLoginOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin_key",
                schema: "identity",
                table: "cli_login_codes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "origin_key",
                schema: "identity",
                table: "cli_login_codes");
        }
    }
}
