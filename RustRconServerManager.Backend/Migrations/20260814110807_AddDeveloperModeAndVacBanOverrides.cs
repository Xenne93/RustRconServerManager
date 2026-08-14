using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustRconServerManager.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDeveloperModeAndVacBanOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeveloperModeEnabled",
                table: "PanelSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DeveloperVacBanOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SteamId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VACBanned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NumberOfVACBans = table.Column<int>(type: "int", nullable: false),
                    DaysSinceLastBan = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperVacBanOverrides", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperVacBanOverrides_SteamId",
                table: "DeveloperVacBanOverrides",
                column: "SteamId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeveloperVacBanOverrides");

            migrationBuilder.DropColumn(
                name: "DeveloperModeEnabled",
                table: "PanelSettings");
        }
    }
}
