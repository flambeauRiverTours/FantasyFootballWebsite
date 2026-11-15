using FantasyLeague.Seasons;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Functions.Functions
{
    /// <summary>
    /// Fills in the games of weeks stored before matchups were read (T68).
    /// </summary>
    /// <param name="seasons">The season service, which owns the backfill.</param>
    public sealed class MatchupBackfillFunctions(
        ISeasonService seasons,
        ILogger<MatchupBackfillFunctions> logger)
    {
        /// <summary>
        /// Reads a few stored weeks' games a day until every season is complete.
        /// </summary>
        /// <param name="timer">The schedule that fired.</param>
        /// <returns>A task that completes when this run's weeks are stored.</returns>
        /// <remarks>
        /// Daily all year, not with Finalize, which only runs September to February.
        /// Runnable by hand from the portal. Once complete, a run reads nothing.
        /// </remarks>
        [Function("BackfillMatchups")]
        public async Task BackfillMatchups(
            [TimerTrigger("0 0 10 * * *")] TimerInfo timer,
            CancellationToken cancellationToken)
        {
            var read = await seasons.BackfillMatchupsAsync(cancellationToken);

            logger.LogInformation("Matchup backfill read {WeekCount} weeks. Next: {Next}.", read, timer.ScheduleStatus?.Next);
        }
    }
}
