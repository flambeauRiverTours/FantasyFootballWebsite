using FantasyLeague.Core;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// A roster whose head-to-head record disagrees with the one Sleeper keeps (T68).
    /// </summary>
    /// <param name="RosterId">The roster whose records differ.</param>
    /// <param name="OurRecord">The record built from each week's games.</param>
    /// <param name="SleeperRecord">Sleeper's own, from the rosters call.</param>
    public sealed record RecordMismatch(int RosterId, TeamRecord OurRecord, TeamRecord SleeperRecord);
}
