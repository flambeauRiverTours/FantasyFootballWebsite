namespace FantasyLeague.Core
{
    /// <summary>
    /// When a week's scores stop changing.
    /// </summary>
    /// <remarks>
    /// A week is final once Sleeper has moved past it. Every caller asks here,
    /// so the rule cannot drift between the build, the payload and the store.
    /// </remarks>
    public static class WeekFinality
    {
        /// <summary>
        /// Gets the last week whose scores are settled.
        /// </summary>
        /// <param name="currentWeek">The week Sleeper considers current.</param>
        /// <returns>The last final week, or 0 before any week has finished.</returns>
        public static int FinalThroughWeek(int currentWeek) => Math.Max(0, currentWeek - 1);

        /// <summary>
        /// Decides whether a week's scores are settled.
        /// </summary>
        /// <param name="week">The week to check.</param>
        /// <param name="currentWeek">The week Sleeper considers current.</param>
        /// <returns><c>true</c> when the week is done.</returns>
        public static bool IsFinal(int week, int currentWeek) => week <= FinalThroughWeek(currentWeek);
    }
}
