namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One entry from <c>/v1/league/{id}/rosters</c>. Used only to join a roster
    /// to its owner.
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
    internal sealed record RosterDto(
        int RosterId,
        string? OwnerId);
}
