using FantasyLeague.Seasons;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Functions.Functions
{
    /// <summary>
    /// Keeps the stored player map current.
    /// </summary>
    /// <param name="players">The refresher.</param>
    public sealed class PlayerMapFunctions(
        IPlayerMapRefresher players,
        ILogger<PlayerMapFunctions> logger)
    {
        /// <summary>
        /// Refetches the dump and replaces the stored map.
        /// </summary>
        /// <param name="timer">The schedule that fired.</param>
        /// <returns>A task that completes when the map has been replaced.</returns>
        [Function("RefreshPlayerMap")]
        public async Task RefreshPlayerMap(
            [TimerTrigger("0 30 9 * * *")] TimerInfo timer,
            CancellationToken cancellationToken)
        {
            await players.RefreshAsync(cancellationToken);

            logger.LogInformation(
                "Player map refresh complete. Next: {Next}.", timer.ScheduleStatus?.Next);
        }
    }
}
