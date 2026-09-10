namespace FantasyLeague.Core
{
    /// <summary>
    /// Represents a fantasy team's performance over a season, including the roster ID, team name, and a list of weekly performance data. Each week contains information about the team's actual and ideal points, starters, ideal lineup, and whether the week's results are final.
    /// </summary>
    /// <param name="RosterId">The ID of the team's roster.</param>
    /// <param name="Name">The name of the team.</param>
    /// <param name="Weeks">The list of weekly performance data.</param>
    public sealed record TeamSeason(
    int RosterId, string Name,
    IReadOnlyList<TeamWeek> Weeks);
}
