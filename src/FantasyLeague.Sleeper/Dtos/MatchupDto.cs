namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One roster's entry in <c>/v1/league/{id}/matchups/{week}</c>.
    /// </summary>
    /// <param name="RosterId">The roster this entry belongs to. Reassigned between seasons, so never used as a cross-season identity.</param>
    /// <param name="MatchupId">Groups the two rosters facing each other. Unused here; the league is decided on total points.</param>
    /// <param name="Points">The roster's score for the week, already scored by Sleeper. Absent, not null, for weeks that have not happened.</param>
    /// <param name="CustomPoints">A commissioner override. Null on an untouched matchup; when set it is the score Sleeper displays, so it takes precedence over <paramref name="Points"/>.</param>
    /// <param name="Players">Every player on the roster that week, starters and bench. This is the roster as it stood then, which is why D2 solves against it rather than the current one.</param>
    /// <param name="Starters">The lineup that was actually started, positionally aligned with the league's startable slots. An empty slot is the string <c>"0"</c>.</param>
    /// <param name="StartersPoints">Parallel to <paramref name="Starters"/>. Summing it should equal <paramref name="Points"/> within a cent.</param>
    /// <param name="PlayersPoints">Points per player id, bench included. A player present in <paramref name="Players"/> but missing here scored zero.</param>
    internal sealed record MatchupDto(
        int RosterId,
        int? MatchupId,
        decimal? Points,
        decimal? CustomPoints,
        IReadOnlyList<string>? Players,
        IReadOnlyList<string>? Starters,
        IReadOnlyList<decimal>? StartersPoints,
        IReadOnlyDictionary<string, decimal>? PlayersPoints);
}
