using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyLeague.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePlayersJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlayersPointsJson",
                table: "WeekScores",
                newName: "PlayersJson");

            // the blob's shape: a player's positions are FantasyPositions, as on RosteredPlayer
            migrationBuilder.Sql(
                "UPDATE WeekScores SET PlayersJson = REPLACE(PlayersJson, '\"Positions\":', '\"FantasyPositions\":')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE WeekScores SET PlayersJson = REPLACE(PlayersJson, '\"FantasyPositions\":', '\"Positions\":')");

            migrationBuilder.RenameColumn(
                name: "PlayersJson",
                table: "WeekScores",
                newName: "PlayersPointsJson");
        }
    }
}
