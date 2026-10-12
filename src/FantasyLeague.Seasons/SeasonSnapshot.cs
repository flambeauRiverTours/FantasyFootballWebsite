using System.ComponentModel;
using FantasyLeague.Core;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// One league season, assembled from Sleeper and ready for a caller to shape.
    /// </summary>
    /// <param name="LeagueName">The league's name, after any configured override.</param>
    /// <param name="Season">The season these weeks belong to.</param>
    /// <param name="CurrentWeek">The week Sleeper considers current.</param>
    /// <param name="FinalThroughWeek">The last week whose scores are settled. Sigma covers only these.</param>
    /// <param name="Slots">Startable slots in roster_positions order, for this season only.</param>
    /// <param name="Teams">Every roster, including excluded ones, so both invariants still cover them.</param>
    /// <param name="UnresolvedPlayerIds">Rostered ids the player map could not explain.</param>
    /// <param name="Violations">Invariant breaches found while assembling.</param>
    /// <param name="ExcludedRosterIds">Rosters left out of every metric (A1), resolved from the configured owner ids.</param>
    /// <remarks>
    /// Excluded rosters are named rather than removed: the payload leaves them
    /// out, and both invariants still check them. Marked immutable so the cache
    /// hands every caller the same instance rather than a serialized copy.
    /// </remarks>
    [ImmutableObject(true)]
    public sealed record SeasonSnapshot(
        string LeagueName,
        int Season,
        int CurrentWeek,
        int FinalThroughWeek,
        IReadOnlyList<Slot> Slots,
        IReadOnlyList<TeamSeason> Teams,
        IReadOnlyList<string> UnresolvedPlayerIds,
        IReadOnlyList<InvariantViolation> Violations,
        IReadOnlySet<int> ExcludedRosterIds);
}
