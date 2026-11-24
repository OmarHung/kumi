using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PmTool.Ai.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_connections_organization_id_name_key",
                schema: "ai",
                table: "connections");

            migrationBuilder.AddColumn<bool>(
                name: "allow_personal_connections",
                schema: "ai",
                table: "settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_person_id",
                schema: "ai",
                table: "connections",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_connections_organization_id_owner_person_id_name_key",
                schema: "ai",
                table: "connections",
                columns: new[] { "organization_id", "owner_person_id", "name_key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_connections_organization_id_owner_person_id_name_key",
                schema: "ai",
                table: "connections");

            migrationBuilder.DropColumn(
                name: "allow_personal_connections",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "owner_person_id",
                schema: "ai",
                table: "connections");

            migrationBuilder.CreateIndex(
                name: "ix_connections_organization_id_name_key",
                schema: "ai",
                table: "connections",
                columns: new[] { "organization_id", "name_key" },
                unique: true);
        }
    }
}
