using FantasyLeague.Core;
using FantasyLeague.Seasons;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Functions.Functions
{
    /// <summary>
    /// Stores each week once its scores stop changing.
    /// </summary>
    /// <param name="seasons">Season assembly for the configured league.</param>
    /// <param name="storedWeeks">Where finalized weeks are kept.</param>
    /// <param name="league">The configured league. Never taken from the trigger.</param>
    /// <param name="time">The clock the next run is counted from.</param>
    public sealed class FinalizeWeekFunctions(
        ISeasonService seasons,
        IFinalizedWeekStore storedWeeks,
        IOptions<LeagueOptions> league,
        TimeProvider time,
        ILogger<FinalizeWeekFunctions> logger)
    {
        /// <summary>
        /// Writes every finalized week of the current season, at noon UTC each
        /// Tuesday from September to February, once Monday night's game is over.
        /// </summary>
        /// <param name="timer">The trigger's binding; its schedule status is not used (see <see cref="FinalizeSchedule.NextRunAfter"/>).</param>
        /// <returns>A task that completes when the weeks are stored.</returns>
        [Function("FinalizeWeek")]
        public async Task FinalizeWeek(
            [TimerTrigger(FinalizeSchedule.Cron)] TimerInfo timer,
            CancellationToken cancellationToken)
        {
            var snapshot = await seasons.BuildAsync(cancellationToken);
            var finalized = snapshot.Teams.SelectMany(t => t.Weeks).Where(w => w.IsFinal).ToList();

            await storedWeeks.WriteAsync(league.Value.CurrentLeagueId, snapshot.Season, finalized, cancellationToken);

            logger.LogInformation("Finalized {WeekCount} team-weeks through week {FinalThroughWeek} of {Season}. Next: {Next}.",
                finalized.Count, snapshot.FinalThroughWeek, snapshot.Season, FinalizeSchedule.NextRunAfter(time.GetUtcNow()));
        }
    }
}
