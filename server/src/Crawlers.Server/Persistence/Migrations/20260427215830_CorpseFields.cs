using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crawlers.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorpseFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeepestFloor",
                table: "corpses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "KillerType",
                table: "corpses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerUsername",
                table: "corpses",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeepestFloor",
                table: "corpses");

            migrationBuilder.DropColumn(
                name: "KillerType",
                table: "corpses");

            migrationBuilder.DropColumn(
                name: "PlayerUsername",
                table: "corpses");
        }
    }
}
