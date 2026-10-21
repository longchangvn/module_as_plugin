using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModuleHostingTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModuleType",
                table: "ModuleVersions",
                type: "text",
                nullable: false,
                defaultValue: "remote");

            migrationBuilder.AddColumn<string>(
                name: "PackagePath",
                table: "ModuleVersions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackageSha256",
                table: "ModuleVersions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModuleType",
                table: "ModuleVersions");

            migrationBuilder.DropColumn(
                name: "PackagePath",
                table: "ModuleVersions");

            migrationBuilder.DropColumn(
                name: "PackageSha256",
                table: "ModuleVersions");
        }
    }
}
