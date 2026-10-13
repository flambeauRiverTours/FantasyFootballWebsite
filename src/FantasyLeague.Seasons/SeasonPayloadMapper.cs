using FantasyLeague.Core;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// Turns an assembled season into the §09 response body.
    /// </summary>
    /// <remarks>
    /// A pure function of its arguments, so it can be tested without a Functions
    /// host. The endpoint supplies the clock rather than this reaching for it.
    /// </remarks>
    public static class SeasonPayloadMapper
    {
        /// <summary>
        /// Maps a snapshot to the season response.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <param name="asOfUtc">When the snapshot was assembled.</param>
        /// <returns>The response body.</returns>
        public static SeasonPayload ToSeasonPayload(SeasonSnapshot snapshot, DateTimeOffset asOfUtc)
        {
            var teams = snapshot.Teams
                .Select(ToTeam)
                .OrderByDescending(t => t.Totals.Actual)
                .ToList();

            var leagueSlotAverages = SlotAverager.LeagueAverages(teams.Select(t => t.SlotAverages));
            return new SeasonPayload(
                snapshot.LeagueName,
                snapshot.Season,
                true, // only the current season is served until T29 (A19)
                snapshot.CurrentWeek,
                snapshot.FinalThroughWeek,
                asOfUtc,
                SlotOrder(snapshot.Slots),
                leagueSlotAverages,
                teams);
        }

        /// <summary>
        /// Maps the seasons that can be served.
        /// </summary>
        /// <param name="current">The live season.</param>
        /// <returns>The response body. Only the current season is served until T29.</returns>
        public static SeasonsPayload ToSeasonsPayload(int current) => new(current, [current]);

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
            [.. slots.Select(s => s.Code).Distinct().Select(Slot.DisplayCode)];

        /// <summary>
        /// Maps one roster's season.
        /// </summary>
        /// <param name="team">The roster.</param>
        /// <returns>The team entry.</returns>
        private static TeamPayload ToTeam(TeamSeason team)
        {
            var spread = SpreadCalculator.Calculate(
                [.. team.Weeks.Where(w => w.IsFinal).Select(w => w.ActualPts)]);

            var totals = new TotalsPayload(
                team.Weeks.Sum(w => w.ActualPts),
                team.Weeks.Sum(w => w.IdealPts),
                spread?.Sigma,
                spread?.Cv,
                spread?.Mean,
                spread?.Min,
                spread?.Max);

            return new TeamPayload(
                team.RosterId,
                team.Name,
                totals,
                SlotAverager.SeasonAverages(team.Weeks)
                    .ToDictionary(kv => Slot.DisplayCode(kv.Key), kv => kv.Value),
                [.. team.Weeks
                    .OrderBy(w => w.Week)
                    .Select(w => new WeekPayload(w.Week, w.ActualPts, w.IdealPts, w.IsFinal))]);
        }

    }
}
