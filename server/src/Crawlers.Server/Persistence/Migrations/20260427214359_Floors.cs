using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crawlers.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Floors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "floors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorNumber = table.Column<int>(type: "integer", nullable: false),
                    Seed = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Tiles = table.Column<byte[]>(type: "bytea", nullable: false),
                    RoomsJson = table.Column<string>(type: "jsonb", nullable: false),
                    BossDoorX = table.Column<int>(type: "integer", nullable: true),
                    BossDoorY = table.Column<int>(type: "integer", nullable: true),
                    BossRoomX = table.Column<int>(type: "integer", nullable: true),
                    BossRoomY = table.Column<int>(type: "integer", nullable: true),
                    BossRoomWidth = table.Column<int>(type: "integer", nullable: true),
                    BossRoomHeight = table.Column<int>(type: "integer", nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_floors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_floors_FloorNumber",
                table: "floors",
                column: "FloorNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "floors");
        }
    }
}
