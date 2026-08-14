using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustRconServerManager.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddLastAnalyticsSentAtToPanelSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastAnalyticsSentAt",
                table: "PanelSettings",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAnalyticsSentAt",
                table: "PanelSettings");
        }
    }
}
