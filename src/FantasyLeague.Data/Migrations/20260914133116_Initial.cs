using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyLeague.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    LeagueId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PreviousLeagueId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Season = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RosterPositionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.LeagueId);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    PlayerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Team = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PositionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.PlayerId);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    LeagueId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RosterId = table.Column<int>(type: "int", nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => new { x.LeagueId, x.RosterId });
                });

            migrationBuilder.CreateTable(
                name: "WeekScores",
                columns: table => new
                {
                    LeagueId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Season = table.Column<int>(type: "int", nullable: false),
                    Week = table.Column<int>(type: "int", nullable: false),
                    RosterId = table.Column<int>(type: "int", nullable: false),
                    ActualPts = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    IdealPts = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    StartersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlayersPointsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdealLineupJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsFinal = table.Column<bool>(type: "bit", nullable: false),
                    ComputedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeekScores", x => new { x.Season, x.LeagueId, x.RosterId, x.Week });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_Season",
                table: "Leagues",
                column: "Season",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Leagues");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "WeekScores");
        }
    }
}
