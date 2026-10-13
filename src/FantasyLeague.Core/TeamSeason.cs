namespace FantasyLeague.Core
{
    /// <summary>
    /// One roster's season.
    /// </summary>
    /// <param name="RosterId">Sleeper's roster id, reassigned between seasons.</param>
    /// <param name="Name">The name to show for the team.</param>
    /// <param name="Weeks">Every week played so far, in order.</param>
    public sealed record TeamSeason(
        int RosterId,
        string Name,
        IReadOnlyList<TeamWeek> Weeks);
}
