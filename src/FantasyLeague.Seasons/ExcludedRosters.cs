using FantasyLeague.Sleeper;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// The outcome of resolving configured owner ids against one season.
    /// </summary>
    /// <param name="RosterIds">Rosters to leave out of every metric (A1).</param>
    /// <param name="UnmatchedOwnerIds">Configured ids this season has no roster for.</param>
    internal sealed record ExcludedRosters(
        IReadOnlySet<int> RosterIds,
        IReadOnlyList<string> UnmatchedOwnerIds)
    {
        /// <summary>
        /// Resolves configured owner ids to this season's roster ids (A1).
        /// </summary>
        /// <param name="teams">This season's teams by roster id.</param>
        /// <param name="ownerIds">The configured owner ids. Empty excludes nobody.</param>
        /// <returns>The rosters to exclude, and every owner id no roster matched.</returns>
        /// <remarks>
        /// Matched on user id because team names change on a whim and roster ids
        /// are reassigned between seasons.
        /// </remarks>
        public static ExcludedRosters Resolve(
            IReadOnlyDictionary<int, SleeperTeam> teams,
            IEnumerable<string> ownerIds)
        {
            var wanted = ownerIds.ToHashSet(StringComparer.Ordinal);
            var rosterIds = new HashSet<int>();
            var found = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (rosterId, team) in teams)
            {
                if (team.OwnerId is { } owner && wanted.Contains(owner))
                {
                    rosterIds.Add(rosterId);
                    found.Add(owner);
                }
            }

            return new ExcludedRosters(rosterIds, [.. wanted.Where(owner => !found.Contains(owner))]);
        }
    }
}
