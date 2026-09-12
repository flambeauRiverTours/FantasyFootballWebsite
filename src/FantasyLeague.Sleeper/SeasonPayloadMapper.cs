using FantasyLeague.Core;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Turns an assembled season into the §09 response body.
    /// </summary>
    /// <remarks>
    /// A pure function of its arguments, so it can be tested without a Functions
    /// host. The endpoint supplies the clock and the exclusion set rather than
    /// this reaching for either.
    /// </remarks>
    public static class SeasonPayloadMapper
    {
        /// <summary>
        /// Maps a snapshot to the season response.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <param name="asOfUtc">When the snapshot was assembled.</param>
        /// <param name="requestedSeason">The season asked for, or null for the current one.</param>
        /// <param name="excludedRosterIds">Rosters to leave out entirely (A1).</param>
        /// <returns>The response body.</returns>
        public static SeasonPayload ToIndividualSeasonPayload(
            SeasonSnapshot snapshot,
            DateTimeOffset asOfUtc,
            int? requestedSeason = null,
            IReadOnlySet<int>? excludedRosterIds = null)
        {
            var excluded = excludedRosterIds ?? new HashSet<int>();

            var teams = snapshot.Teams
                .Where(t => !excluded.Contains(t.RosterId))
                .Select(ToTeam)
                .OrderByDescending(t => t.Totals.Actual)
                .ToList();

            return new SeasonPayload(
                snapshot.Season,
                requestedSeason is null || requestedSeason == snapshot.Season,
                snapshot.CurrentWeek,
                snapshot.FinalThroughWeek,
                asOfUtc,
                SlotOrder(snapshot.Slots),
                // M6 fills this; an empty object beats omitting the key
                new Dictionary<string, decimal>(),
                teams);
        }

        /// <summary>
        /// Maps the seasons that can be served.
        /// </summary>
        /// <param name="current">The live season.</param>
        /// <param name="available">Every season with data, or null for just the current one.</param>
        /// <returns>The response body.</returns>
        public static SeasonsPayload ToSeasonsPayload(int current, IEnumerable<int>? available = null) =>
            new(current, [.. (available ?? [current]).Distinct().OrderByDescending(s => s)]);

        /// <summary>
        /// Reduces the startable slots to the distinct codes, in roster_positions order.
        /// </summary>
        /// <param name="slots">Every startable slot, duplicates included.</param>
        /// <returns>The radar's axis order.</returns>
        /// <remarks>
        /// Eleven slots become eight axes for this league. R2: the silhouette
        /// depends on axis order, so this order is fixed and never re-sorted.
        /// </remarks>
        private static IReadOnlyList<string> SlotOrder(IReadOnlyList<Slot> slots) =>
            [.. slots.Select(s => s.Code).Distinct()];

        /// <summary>
        /// Maps one roster's season.
        /// </summary>
        /// <param name="team">The roster.</param>
        /// <returns>The team entry.</returns>
        private static TeamPayload ToTeam(TeamSeason team)
        {
            var spread = SpreadCalculator.Calculate(
                [.. team.Weeks.Where(w => w.IsFinal).Select(w => w.Actual)]);

            var totals = new TotalsPayload(
                team.Weeks.Sum(w => w.Actual),
                team.Weeks.Sum(w => w.Ideal),
                spread?.Sigma,
                spread?.Cv,
                spread?.Mean,
                spread?.Min,
                spread?.Max);

            return new TeamPayload(
                team.RosterId,
                team.Name,
                totals,
                new Dictionary<string, decimal>(),
                [.. team.Weeks
                    .OrderBy(w => w.Week)
                    .Select(w => new WeekPayload(w.Week, w.Actual, w.Ideal, w.IsFinal))]);
        }
    }
}
