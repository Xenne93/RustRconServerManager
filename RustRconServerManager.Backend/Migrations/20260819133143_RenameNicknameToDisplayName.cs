using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustRconServerManager.Backend.Migrations
{
    /// <inheritdoc />
    public partial class RenameNicknameToDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Nickname",
                table: "AspNetUsers",
                newName: "DisplayName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DisplayName",
                table: "AspNetUsers",
                newName: "Nickname");
        }
    }
}
