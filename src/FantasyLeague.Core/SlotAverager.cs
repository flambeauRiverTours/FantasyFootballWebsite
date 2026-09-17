namespace FantasyLeague.Core
{
    public static class SlotAverager
    {

        /// <summary>
        /// Averages starter player scores by slot
        /// </summary>
        /// <param name="starters">List of starters</param>
        /// <param name="playersPoints">Dict of player to points scored</param>
        /// <param name="playerPositions">Dict of playet to positions</param>
        /// <param name="startableSlots">List of startable slots</param>
        /// <returns>Dictionary of slot to average points for that slot</returns>
        public static IReadOnlyDictionary<string, decimal>? MakeSlotAverages(
            IReadOnlyList<string> starters,
            IReadOnlyDictionary<string, decimal> playersPoints,
            IReadOnlyDictionary<string, IReadOnlyList<string>> playerPositions,
            IReadOnlyList<Slot> startableSlots)
        {
            if (starters.Count != startableSlots.Count) return null;   // rules changed mid-season

            var byCode = new Dictionary<string, (decimal Total, int Slots)>();
            for (var i = 0; i < starters.Count; i++)
            {
                var slot = startableSlots[i];
                var starterId = starters[i];

                if (starterId != "0"
                    && playerPositions.TryGetValue(starterId, out var positions)
                    && !slot.Accepts(positions))
                {
                    return null;
                }

                var pts = starterId == "0"                            // an empty slot
                         ? 0m
                         : playersPoints.GetValueOrDefault(starterId, 0m);

                var cur = byCode.GetValueOrDefault(slot.Code);
                byCode[slot.Code] = (cur.Total + pts, cur.Slots + 1);
            }

            return byCode.ToDictionary(kv => kv.Key, kv => kv.Value.Total / kv.Value.Slots);
        }

        /// <summary>
        /// Averages one team's per-week slot values over finalized weeks that
        /// produced an attribution. A week the mid-season rules guard excluded
        /// carries an empty slot average and is skipped rather than counted as a
        /// zero at every slot.
        /// </summary>
        public static IReadOnlyDictionary<string, decimal> SeasonAverages(IEnumerable<TeamWeek> weeks)
        {
            var byCode = new Dictionary<string, (decimal Total, int Weeks)>();

            foreach (var week in weeks.Where(w => w.IsFinal && w.SlotAverages.Count > 0))
            {
                foreach (var (code, value) in week.SlotAverages)
                {
                    var cur = byCode.GetValueOrDefault(code);
                    byCode[code] = (cur.Total + value, cur.Weeks + 1);
                }
            }

            return byCode.ToDictionary(kv => kv.Key, kv => kv.Value.Total / kv.Value.Weeks);
        }

        /// <summary>
        /// The radar's reference circle: each slot averaged across the given
        /// teams' season values. Average first, then divide (T47) - this takes
        /// season totals already averaged over weeks, never a per-week ratio.
        /// Callers filter to non-excluded teams first (A1); this function does
        /// not know about the dummy team.
        /// </summary>
        public static IReadOnlyDictionary<string, decimal> LeagueAverages(
            IEnumerable<IReadOnlyDictionary<string, decimal>> teamSeasonAverages)
        {
            var byCode = new Dictionary<string, (decimal Total, int Teams)>();

            foreach (var team in teamSeasonAverages)
            {
                foreach (var (code, value) in team)
                {
                    var cur = byCode.GetValueOrDefault(code);
                    byCode[code] = (cur.Total + value, cur.Teams + 1);
                }
            }

            return byCode.ToDictionary(kv => kv.Key, kv => kv.Value.Total / kv.Value.Teams);
        }
    }
}
