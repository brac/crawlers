using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crawlers.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Corpses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corpses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FloorNumber = table.Column<int>(type: "integer", nullable: false),
                    X = table.Column<int>(type: "integer", nullable: false),
                    Y = table.Column<int>(type: "integer", nullable: false),
                    DiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CauseOfDeath = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corpses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_corpses_FloorNumber_X_Y",
                table: "corpses",
                columns: new[] { "FloorNumber", "X", "Y" });

            migrationBuilder.CreateIndex(
                name: "IX_corpses_PlayerId",
                table: "corpses",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corpses");
        }
    }
}
