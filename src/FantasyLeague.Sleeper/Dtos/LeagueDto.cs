namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// <c>/v1/league/{id}</c> — the league's own settings.
    /// </summary>
    /// <param name="Name">The league's display name.</param>
    /// <param name="RosterPositions">Every slot, startable and bench, one entry per slot. The startable ones drive both the solver and the radar axes, and this is per-season data (A3).</param>
    /// <param name="Season">The season this league plays. Sent as a string; binds through <see cref="SleeperJson.Options"/>.</param>
    /// <param name="PreviousLeagueId">The league this one rolled over from, or null for a first season (A4). The season chain walks it (D8).</param>
    internal sealed record LeagueDto(
        string? Name,
        IReadOnlyList<string>? RosterPositions,
        int Season,
        string? PreviousLeagueId);
}
