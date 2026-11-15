namespace FantasyLeague.Core
{
    /// <summary>
    /// Which weeks count: the regular season only (Q4, A33).
    /// </summary>
    /// <remarks>
    /// Playoff weeks are still read and stored, because the bracket and a past
    /// season's champion need them (T69, T70). They are left out of totals,
    /// sigma, the chart and the radar, and every caller asks here, so the
    /// cut-off cannot drift between them.
    /// </remarks>
    public static class RegularSeason
    {
        /// <summary>
        /// The NFL's regular season since 2021, used when a league sets no playoffs.
        /// </summary>
        public const int NFLWeeks = 18;

        /// <summary>
        /// Gets the last week that counts.
        /// </summary>
        /// <param name="playoffWeekStart">The league's first playoff week as Sleeper sends it: zero, or null, when it has none.</param>
        /// <returns>The week before the playoffs, else the NFL's last regular week.</returns>
        public static int LastWeek(int? playoffWeekStart) =>
            playoffWeekStart is > 1 and var start ? Math.Min(start - 1, NFLWeeks) : NFLWeeks;

        /// <summary>
        /// Gets the last week that counts once weeks nobody played are taken off the end.
        /// </summary>
        /// <param name="lastWeek">The cut-off from <see cref="LastWeek"/>.</param>
        /// <param name="finalThroughWeek">The last settled week.</param>
        /// <param name="weeks">Every counted roster's weeks.</param>
        /// <returns>The cut-off, less any final weeks at its end where no roster scored.</returns>
        /// <remarks>
        /// Sleeper's league settings do not say which week a league without playoffs
        /// ends (A37), so <see cref="NFLWeeks"/> is assumed. A league whose season ends sooner gets a
        /// settled week of zeros, or no week at all, which would otherwise count as
        /// a real week in sigma and the chart. A real week never scores zero for
        /// everyone. Only weeks at the end are dropped, and only once settled, so a
        /// season in progress is never shortened.
        /// </remarks>
        public static int LastCountedWeek(int lastWeek, int finalThroughWeek, IEnumerable<TeamWeek> weeks) =>
            finalThroughWeek < lastWeek
                ? lastWeek
                : weeks.Where(w => w.Week <= lastWeek && w.ActualPts != 0m).Select(w => w.Week).DefaultIfEmpty(1).Max();
    }
}
