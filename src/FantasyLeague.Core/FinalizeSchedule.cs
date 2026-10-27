namespace FantasyLeague.Core
{
    /// <summary>
    /// When finished weeks are written to the store: Tuesdays at 12:00 UTC,
    /// September to February, once Monday night's game is over.
    /// </summary>
    /// <remarks>
    /// The timer that runs the job and the reader that judges whether a stored
    /// season predates the latest run both read it here, so they cannot drift.
    /// </remarks>
    public static class FinalizeSchedule
    {
        /// <summary>The timer's NCRONTAB expression: second, minute, hour, day, month, weekday.</summary>
        public const string Cron = "0 0 12 * 9,10,11,12,1,2 2";

        /// <summary>The hour, in UTC, the job runs.</summary>
        private const int Hour = 12;

        /// <summary>The months the job runs in; the offseason costs nothing.</summary>
        private static readonly int[] Months = [9, 10, 11, 12, 1, 2];

        /// <summary>
        /// Gets the latest scheduled run at or before a moment.
        /// </summary>
        /// <param name="now">The moment to look back from.</param>
        /// <returns>The run's start, in UTC.</returns>
        /// <remarks>
        /// In the offseason that is the last Tuesday of February, so a season
        /// stored after it stays current until September.
        /// </remarks>
        public static DateTimeOffset LastRunAtOrBefore(DateTimeOffset now)
        {
            var utc = now.ToUniversalTime();
            var run = new DateTimeOffset(utc.Year, utc.Month, utc.Day, Hour, 0, 0, TimeSpan.Zero);

            while (run > utc || run.DayOfWeek != DayOfWeek.Tuesday || !Months.Contains(run.Month))
            {
                run = run.AddDays(-1);
            }

            return run;
        }

        /// <summary>
        /// Gets the first scheduled run strictly after a moment.
        /// </summary>
        /// <param name="now">The moment to look forward from.</param>
        /// <returns>The run's start, in UTC.</returns>
        /// <remarks>
        /// The timer's own <c>ScheduleStatus.Next</c> reports the run in progress,
        /// not the one after it, so the job logs this instead.
        /// </remarks>
        public static DateTimeOffset NextRunAfter(DateTimeOffset now)
        {
            var utc = now.ToUniversalTime();
            var run = new DateTimeOffset(utc.Year, utc.Month, utc.Day, Hour, 0, 0, TimeSpan.Zero);

            while (run <= utc || run.DayOfWeek != DayOfWeek.Tuesday || !Months.Contains(run.Month))
            {
                run = run.AddDays(1);
            }

            return run;
        }
    }
}
