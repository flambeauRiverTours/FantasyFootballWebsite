using System.ComponentModel;
using FantasyLeague.Core;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One league season, assembled from Sleeper and ready for a caller to shape.
    /// </summary>
    /// <param name="LeagueName">The name of the league</param>
    /// <param name="Season">The season these weeks belong to.</param>
    /// <param name="CurrentWeek">The week Sleeper considers current.</param>
    /// <param name="FinalThroughWeek">The last week whose scores are settled. Sigma covers only these.</param>
    /// <param name="Slots">Startable slots in roster_positions order, for this season only.</param>
    /// <param name="Teams">Every roster, including any the caller means to exclude.</param>
    /// <param name="UnresolvedPlayerIds">Rostered ids the player map could not explain.</param>
    /// <param name="Violations">Invariant breaches found while assembling.</param>
    /// <param name="RosterOwnerIds">Roster id to owning user id. The only identifier stable across seasons, and what A1 matches on.</param>
    /// <remarks>
    /// Reports facts; the caller decides policy. Marked immutable so the cache
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
        IReadOnlyDictionary<int, string> RosterOwnerIds);

    /// <summary>
    /// A team-week where one of the two invariants did not hold.
    /// </summary>
    /// <param name="Week">The week.</param>
    /// <param name="RosterId">The roster.</param>
    /// <param name="Invariant">Which check failed.</param>
    /// <param name="Expected">What it should have been.</param>
    /// <param name="Actual">What it was.</param>
    public sealed record InvariantViolation(
        int Week,
        int RosterId,
        InvariantKind Invariant,
        decimal Expected,
        decimal Actual);

    /// <summary>
    /// The outcome of resolving configured owner ids against one season.
    /// </summary>
    /// <param name="RosterIds">Rosters to leave out of every metric (A1).</param>
    /// <param name="UnmatchedOwnerIds">Configured ids this season has no roster for.</param>
    public sealed record ExcludedRosters(
        IReadOnlySet<int> RosterIds,
        IReadOnlyList<string> UnmatchedOwnerIds);

    /// <summary>
    /// The two checks the payload is self-verifying against.
    /// </summary>
    public enum InvariantKind
    {
        /// <summary>sum(starters_points) == points, within a cent.</summary>
        StartersSumMatchesPoints,

        /// <summary>ideal &gt;= Sleeper's scored points, not a commissioner override. Catches restrictive eligibility only; a permissive slot map inflates ideal undetected.</summary>
        IdealNotBelowActual,
    }
}
