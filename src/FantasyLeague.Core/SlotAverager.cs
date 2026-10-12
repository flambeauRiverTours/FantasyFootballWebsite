namespace FantasyLeague.Core
{
    /// <summary>
    /// The radar's figures: points per startable slot, averaged per week, per season and across the league.
    /// </summary>
    public static class SlotAverager
    {
        /// <summary>
        /// Averages one week's starter points by slot.
        /// </summary>
        /// <param name="starters">The lineup in slot order, with null for a slot left empty.</param>
        /// <param name="playersPoints">Points per player id. A missing id scored zero.</param>
        /// <param name="playerPositions">Fantasy positions per player id.</param>
        /// <param name="startableSlots">The season's startable slots, in roster_positions order.</param>
        /// <returns>
        /// Average points per slot code, or null when the starters do not line up
        /// with the slots: a different count, or a starter ineligible for the slot
        /// at its position. Both mean the slot list changed after this week.
        /// </returns>
        public static IReadOnlyDictionary<string, decimal>? WeekAverages(
            IReadOnlyList<string?> starters,
            IReadOnlyDictionary<string, decimal> playersPoints,
            IReadOnlyDictionary<string, IReadOnlyList<string>> playerPositions,
            IReadOnlyList<Slot> startableSlots)
        {
            if (starters.Count != startableSlots.Count)
            {
                return null;
            }

            var points = new List<(string Code, decimal Value)>(starters.Count);
            for (var i = 0; i < starters.Count; i++)
            {
                var slot = startableSlots[i];
                var starter = starters[i];

                if (starter is not null
                    && playerPositions.TryGetValue(starter, out var positions)
                    && !slot.Accepts(positions))
                {
                    return null;
                }

                // an empty slot is a real zero and still counts toward the divisor
                points.Add((slot.Code, starter is null ? 0m : playersPoints.GetValueOrDefault(starter, 0m)));
            }

            return Mean(points);
        }

        /// <summary>
        /// Averages one team's per-week slot values over finalized weeks that
        /// produced an attribution. A week the mid-season rules guard excluded
        /// carries an empty slot average and is skipped rather than counted as a
        /// zero at every slot.
        /// </summary>
        /// <param name="weeks">The team's weeks.</param>
        /// <returns>Average points per slot code across the season.</returns>
        public static IReadOnlyDictionary<string, decimal> SeasonAverages(IEnumerable<TeamWeek> weeks) =>
            Mean(weeks
                .Where(w => w.IsFinal && w.SlotAverages.Count > 0)
                .SelectMany(w => w.SlotAverages.Select(kv => (kv.Key, kv.Value))));

        /// <summary>
        /// The radar's reference circle: each slot averaged across the given
        /// teams' season values. Average first, then divide (T47) - this takes
        /// season totals already averaged over weeks, never a per-week ratio.
        /// Callers filter to non-excluded teams first (A1); this function does
        /// not know about the dummy team.
        /// </summary>
        /// <param name="teamSeasonAverages">Each team's season averages.</param>
        /// <returns>Average points per slot code across the teams.</returns>
        public static IReadOnlyDictionary<string, decimal> LeagueAverages(
            IEnumerable<IReadOnlyDictionary<string, decimal>> teamSeasonAverages) =>
            Mean(teamSeasonAverages.SelectMany(team => team.Select(kv => (kv.Key, kv.Value))));

        /// <summary>
        /// Averages values that share a slot code.
        /// </summary>
        /// <param name="values">Slot code and value pairs, any number per code.</param>
        /// <returns>The mean value per code.</returns>
        private static IReadOnlyDictionary<string, decimal> Mean(IEnumerable<(string Code, decimal Value)> values) =>
            values
                .GroupBy(v => v.Code)
                .ToDictionary(g => g.Key, g => g.Average(v => v.Value));
    }
}
