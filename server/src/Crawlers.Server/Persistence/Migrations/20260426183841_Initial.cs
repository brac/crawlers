using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crawlers.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "run_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seed = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CauseOfDeath = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeepestFloor = table.Column<int>(type: "integer", nullable: false),
                    EnemiesKilled = table.Column<int>(type: "integer", nullable: false),
                    FinalHp = table.Column<int>(type: "integer", nullable: false),
                    FinalMaxHp = table.Column<int>(type: "integer", nullable: false),
                    InventoryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_run_history", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_run_history_EndedAt",
                table: "run_history",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_run_history_PlayerId",
                table: "run_history",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "run_history");
        }
    }
}
