using FantasyLeague.Core;
using FantasyLeague.Sleeper;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// A roster whose head-to-head record disagrees with the one Sleeper keeps (T68).
    /// </summary>
    /// <param name="RosterId">The roster whose records differ.</param>
    /// <param name="OurRecord">The record built from each week's games.</param>
    /// <param name="SleeperRecord">Sleeper's own, from the rosters call.</param>
    public sealed record RecordMismatch(int RosterId, TeamRecord OurRecord, TeamRecord SleeperRecord)
    {
        /// <summary>
        /// Compares our records with Sleeper's, the third check beside the two invariants.
        /// </summary>
        /// <param name="ours">Our records, or null when they are incomplete.</param>
        /// <param name="teams">The league's rosters, with Sleeper's records.</param>
        /// <returns>Every roster whose wins, losses or ties differ.</returns>
        /// <remarks>
        /// Only rosters where both sides count the same number of results are
        /// compared: Sleeper updates its records on its own schedule, so a week
        /// one side has counted and the other has not is a lag, not a breach.
        /// Points are not compared; a stat correction after a week is stored
        /// moves Sleeper's and not ours.
        /// </remarks>
        public static IReadOnlyList<RecordMismatch> Find(
            IReadOnlyDictionary<int, TeamRecord>? ours,
            IReadOnlyDictionary<int, SleeperTeam> teams) =>
            ours is null
                ? []
                : [.. ours
                    .Where(o => teams.TryGetValue(o.Key, out var team)
                        && team.Record is { } theirs
                        && theirs.Games == o.Value.Games
                        && (theirs.Wins, theirs.Losses, theirs.Ties) != (o.Value.Wins, o.Value.Losses, o.Value.Ties))
                    .OrderBy(o => o.Key)
                    .Select(o => new RecordMismatch(o.Key, o.Value, teams[o.Key].Record!))];
    }
}
