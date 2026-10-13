namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// <c>/v1/league/{id}</c> — the league's own settings.
    /// </summary>
    /// <remarks>
    /// Only the fields the mapper reads. T29's chain walk adds
    /// <c>previous_league_id</c> back.
    /// </remarks>
    /// <param name="Name">The league's display name.</param>
    /// <param name="RosterPositions">Every slot, startable and bench, one entry per slot. The startable ones drive both the solver and the radar axes, and this is per-season data (A3).</param>
    internal sealed record LeagueDto(
        string? Name,
        IReadOnlyList<string>? RosterPositions);
}
