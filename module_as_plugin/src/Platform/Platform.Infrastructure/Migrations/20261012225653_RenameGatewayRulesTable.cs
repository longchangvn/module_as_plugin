using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameGatewayRulesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_GatewayRules",
                table: "GatewayRules");

            migrationBuilder.RenameTable(
                name: "GatewayRules",
                newName: "PlatformGatewayRules");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PlatformGatewayRules",
                table: "PlatformGatewayRules",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PlatformGatewayRules",
                table: "PlatformGatewayRules");

            migrationBuilder.RenameTable(
                name: "PlatformGatewayRules",
                newName: "GatewayRules");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GatewayRules",
                table: "GatewayRules",
                column: "Id");
        }
    }
}
