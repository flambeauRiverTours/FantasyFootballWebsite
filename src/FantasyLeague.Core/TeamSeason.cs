namespace FantasyLeague.Core
{
    /// <summary>
    /// One roster's season.
    /// </summary>
    /// <param name="RosterId">Sleeper's roster id, reassigned between seasons.</param>
    /// <param name="Name">The name to show for the team.</param>
    /// <param name="Weeks">Every week played so far, in order.</param>
    /// <param name="Division">The roster's division number as Sleeper sent it (T72b), or null.</param>
    public sealed record TeamSeason(
        int RosterId,
        string Name,
        IReadOnlyList<TeamWeek> Weeks,
        int? Division = null);
}
