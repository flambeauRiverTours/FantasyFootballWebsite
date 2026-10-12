using FantasyLeague.Core;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One roster's week, mapped out of Sleeper's shapes and into the domain, with
    /// the things that did not resolve carried alongside rather than dropped.
    /// </summary>
    /// <param name="RosterId">The roster this week belongs to.</param>
    /// <param name="ActualPts">What the roster actually scored, taking a commissioner override into account.</param>
    /// <param name="ScoredPts">Sleeper's own scored total, before any override. What both invariants compare against (A9).</param>
    /// <param name="Players">Every rostered player that week, ready for the solver.</param>
    /// <param name="Starters">The lineup that was started, in slot order, with empty slots removed.</param>
    /// <param name="StartersPoints">Points for the started lineup, for the <c>sum(starters_points) == points</c> invariant.</param>
    /// <param name="SlotStarters">The lineup aligned with the startable slots, with null for a slot left empty.</param>
    /// <param name="PlayersPoints">Points per player id, bench included. A missing id scored zero.</param>
    /// <param name="UnresolvedPlayerIds">
    /// Ids present on the roster that the player map could not explain.
    /// </param>
    /// <remarks>
    /// <see cref="UnresolvedPlayerIds"/> exists so an unknown player is a fact the
    /// caller has to deal with rather than a silent subtraction. The player map is
    /// refreshed daily, so a player signed and rostered between refreshes will
    /// genuinely be missing for a few hours — dropping the player quietly would lower
    /// that team's ideal points, and the <c>ideal &gt;= actual</c> invariant would
    /// not catch it, because undercounting is the direction that invariant cannot
    /// see.
    /// </remarks>
    public sealed record RosterWeek(
        int RosterId,
        decimal ActualPts,
        decimal ScoredPts,
        IReadOnlyList<RosteredPlayer> Players,
        IReadOnlyList<PlayerId> Starters,
        IReadOnlyList<decimal> StartersPoints,
        IReadOnlyList<string?> SlotStarters,
        IReadOnlyDictionary<string, decimal> PlayersPoints,
        IReadOnlyList<string> UnresolvedPlayerIds);
}
