using FantasyLeague.Core;
using FantasyLeague.Sleeper;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// Our head-to-head records against Sleeper's (T68), counted, so a clean check can be told from one that compared nothing (T72a).
    /// </summary>
    /// <param name="Compared">Rosters whose records were compared.</param>
    /// <param name="Skipped">Rosters left out: Sleeper sent no record, or has counted a different number of games.</param>
    /// <param name="Mismatches">Compared rosters whose wins, losses or ties differ.</param>
    public sealed record RecordCheck(int Compared, int Skipped, IReadOnlyList<RecordMismatch> Mismatches)
    {
        /// <summary>
        /// Compares our records with Sleeper's, the third check beside the two invariants.
        /// </summary>
        /// <param name="ours">Our records, or null when they are incomplete.</param>
        /// <param name="teams">The league's rosters, with Sleeper's records.</param>
        /// <returns>The check, or null when our records are incomplete and nothing could be compared.</returns>
        /// <remarks>
        /// Only rosters where both sides count the same number of results are
        /// compared: Sleeper updates its records on its own schedule, so a week
        /// one side has counted and the other has not is a lag, not a breach.
        /// Points are not compared; a stat correction after a week is stored
        /// moves Sleeper's and not ours.
        /// </remarks>
        public static RecordCheck? Run(
            IReadOnlyDictionary<int, TeamRecord>? ours,
            IReadOnlyDictionary<int, SleeperTeam> teams)
        {
            if (ours is null)
            {
                return null;
            }

            var compared = ours
                .Select(o => (RosterId: o.Key, Ours: o.Value, Theirs: teams.GetValueOrDefault(o.Key)?.Record))
                .Where(o => o.Theirs is { } theirs && theirs.Games == o.Ours.Games)
                .ToList();
            // Theirs is never null here: the Where above kept only rosters Sleeper sent a record for
            var mismatches = compared
                .Where(o => (o.Theirs!.Wins, o.Theirs.Losses, o.Theirs.Ties) != (o.Ours.Wins, o.Ours.Losses, o.Ours.Ties))
                .OrderBy(o => o.RosterId)
                .Select(o => new RecordMismatch(o.RosterId, o.Ours, o.Theirs!))
                .ToList();

            return new RecordCheck(compared.Count, ours.Count - compared.Count, mismatches);
        }
    }
}
