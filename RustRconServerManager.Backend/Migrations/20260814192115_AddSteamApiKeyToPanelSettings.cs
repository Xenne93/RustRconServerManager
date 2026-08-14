using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustRconServerManager.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamApiKeyToPanelSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SteamApiKeyEncrypted",
                table: "PanelSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SteamApiKeyEncrypted",
                table: "PanelSettings");
        }
    }
}
