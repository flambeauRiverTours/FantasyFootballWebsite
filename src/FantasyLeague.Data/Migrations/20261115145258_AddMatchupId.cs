using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyLeague.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchupId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MatchupId",
                table: "WeekScores",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchupId",
                table: "WeekScores");
        }
    }
}
