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
        /// <param name="rosterOwnerIds">Owner user ids by roster id.</param>
        /// <param name="ownerIds">The configured owner ids. Empty excludes nobody.</param>
        /// <returns>The rosters to exclude, and every owner id no roster matched.</returns>
        /// <remarks>
        /// Matched on user id because team names change on a whim and roster ids
        /// are reassigned between seasons.
        /// </remarks>
        public static ExcludedRosters Resolve(
            IReadOnlyDictionary<int, string> rosterOwnerIds,
            IEnumerable<string> ownerIds)
        {
            var owners = ownerIds.ToHashSet(StringComparer.Ordinal);
            var matched = rosterOwnerIds.Where(pair => owners.Contains(pair.Value)).ToList();

            return new ExcludedRosters(
                matched.Select(pair => pair.Key).ToHashSet(),
                [.. owners.Except(matched.Select(pair => pair.Value), StringComparer.Ordinal)]);
        }
    }
}
