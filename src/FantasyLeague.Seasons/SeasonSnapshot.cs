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
    /// <param name="Teams">Every roster that counts. A roster excluded by A1 is never here.</param>
    /// <param name="UnresolvedPlayerIds">Rostered ids the player map could not explain, excluded rosters included.</param>
    /// <param name="Violations">Invariant breaches found while assembling, excluded rosters included.</param>
    /// <remarks>
    /// An excluded roster is left out of every metric but still checked: a
    /// solver bug shows on the placeholder team's lineup as well as any other.
    /// Marked immutable so the cache hands every caller the same instance
    /// rather than a serialized copy.
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
        IReadOnlyList<InvariantViolation> Violations);
}
