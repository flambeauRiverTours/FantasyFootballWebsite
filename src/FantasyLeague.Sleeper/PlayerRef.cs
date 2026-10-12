namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// The three fields of a Sleeper player that anything downstream actually
    /// needs, projected out of the 14 MB dump.
    /// </summary>
    /// <remarks>
    /// Public because the daily refresh job caches a map of these and the season
    /// assembly reads it. The dump itself must never sit on a request path.
    /// </remarks>
    /// <param name="Name">The player's name, falling back to the id when Sleeper has none.</param>
    /// <param name="Team">The NFL team abbreviation, or null for a free agent.</param>
    /// <param name="FantasyPositions">Every position the player is eligible at. Never empty — entries without positions are dropped during projection, since nothing can be started at no position.</param>
    public sealed record PlayerRef(
        string Name,
        string? Team,
        IReadOnlyList<string> FantasyPositions);

    /// <summary>
    /// One roster's week, mapped out of Sleeper's shapes and into the domain, with
    /// the things that did not resolve carried alongside rather than dropped.
    /// </summary>
    /// <param name="RosterId">The roster this week belongs to.</param>
    /// <param name="Actual">What the roster actually scored, taking a commissioner override into account.</param>
    /// <param name="Points">Sleeper's own scored total, before any override. What the starters-sum invariant compares against.</param>
    /// <param name="Players">Every rostered player that week, ready for the solver.</param>
    /// <param name="Starters">The lineup that was started, in slot order, with empty slots removed.</param>
    /// <param name="StartersPoints">Points for the started lineup, for the <c>sum(starters_points) == points</c> invariant.</param>
    /// <param name="UnresolvedPlayerIds">
    /// Ids present on the roster that the player map could not explain.
    /// </param>
    /// <remarks>
    /// <see cref="UnresolvedPlayerIds"/> exists so an unknown player is a fact the
    /// caller has to deal with rather than a silent subtraction. The player map is
    /// refreshed daily, so a player signed and rostered between refreshes will
    /// genuinely be missing for a few hours — dropping him quietly would lower
    /// that team's ideal points, and the <c>ideal &gt;= actual</c> invariant would
    /// not catch it, because undercounting is the direction that invariant cannot
    /// see.
    /// </remarks>
    internal sealed record RosterWeek(
        int RosterId,
        decimal Actual,
        decimal Points,
        IReadOnlyList<FantasyLeague.Core.RosteredPlayer> Players,
        IReadOnlyList<FantasyLeague.Core.PlayerId> Starters,
        IReadOnlyList<decimal> StartersPoints,
        IReadOnlyList<string> UnresolvedPlayerIds);
}
