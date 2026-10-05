using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyLeague.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlotAveragesJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SlotAveragesJson",
                table: "WeekScores",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SlotAveragesJson",
                table: "WeekScores");
        }
    }
}
