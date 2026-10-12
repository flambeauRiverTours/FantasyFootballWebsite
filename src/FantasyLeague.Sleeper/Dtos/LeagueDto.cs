namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// <c>/v1/league/{id}</c> — the league's own settings.
    /// </summary>
    /// <remarks>
    /// This object carries <see cref="LeagueId"/>, which is a secret (A2). Persist
    /// the fields that are needed, never the object whole, and never write it to a
    /// fixture.
    /// </remarks>
    /// <param name="LeagueId">Sleeper's id for this league in this season. Not stable across seasons — a rollover mints a new one.</param>
    /// <param name="PreviousLeagueId">The league this one rolled over from, or null. D8 walks this backwards; null legitimately means a first season (A4).</param>
    /// <param name="Season">The season this league plays. Sent as a string.</param>
    /// <param name="Name">The league's display name.</param>
    /// <param name="Status">e.g. <c>in_season</c>, <c>complete</c>.</param>
    /// <param name="TotalRosters">How many rosters the league has. Six here, of which five score (A1).</param>
    /// <param name="RosterPositions">Every slot, startable and bench, one entry per slot. The startable ones drive both the solver and the radar axes, and this is per-season data (A3).</param>
    internal sealed record LeagueDto(
        string LeagueId,
        string? PreviousLeagueId,
        int Season,
        string? Name,
        string? Status,
        int? TotalRosters,
        IReadOnlyList<string>? RosterPositions);
}
