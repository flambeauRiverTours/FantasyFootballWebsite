namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One entry from <c>/v1/league/{id}/rosters</c>. Used to join a roster to
    /// its owner, and for Sleeper's own record (T68).
    /// </summary>
    /// <remarks>
    /// This object also carries <c>players</c> and <c>starters</c>, and they are
    /// the roster <em>as it stands today</em>. D2 is explicit that the ideal
    /// lineup must be solved against <see cref="MatchupDto.Players"/> instead, or
    /// every waiver claim silently rewrites the league's history. They are left
    /// off this DTO so the wrong one cannot be reached for by accident.
    /// </remarks>
    /// <param name="RosterId">The roster's id within this league and season.</param>
    /// <param name="OwnerId">The owning user. Null for an orphaned roster with no manager.</param>
    /// <param name="Settings">The roster's settings, of which the record and the division are read.</param>
    internal sealed record RosterDto(
        int RosterId,
        string? OwnerId,
        RosterSettingsDto? Settings);

    /// <summary>
    /// The part of a roster's <c>settings</c> object this app reads: Sleeper's record so far, and its division.
    /// </summary>
    /// <param name="Wins">Wins, median results included.</param>
    /// <param name="Losses">Losses, median results included.</param>
    /// <param name="Ties">Ties, median results included.</param>
    /// <param name="Fpts">Whole points scored.</param>
    /// <param name="FptsDecimal">The hundredths of <paramref name="Fpts"/>.</param>
    /// <param name="FptsAgainst">Whole points scored against.</param>
    /// <param name="FptsAgainstDecimal">The hundredths of <paramref name="FptsAgainst"/>.</param>
    /// <param name="Division">The roster's division, from 1 (T72b). Read only when the league has divisions.</param>
    internal sealed record RosterSettingsDto(
        int? Wins,
        int? Losses,
        int? Ties,
        int? Fpts,
        int? FptsDecimal,
        int? FptsAgainst,
        int? FptsAgainstDecimal,
        int? Division = null);
}
