using FantasyLeague.Core;
using FantasyLeague.Sleeper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Functions.Functions
{
    /// <summary>
    /// Saves data for the week of football of to the database 
    /// </summary>
    /// <param name="seasons">SeasonService to save off</param>
    /// <param name="storedWeeks">Cache of week data to store off</param>
    /// <param name="league">The configured league. Never taken from the trigger.</param>
    /// <param name="logger">Logger.</param>
    public sealed class FinalizeWeek(ISeasonService seasons, IFinalizedWeekStore storedWeeks, IOptions<LeagueOptions> league,
        ILogger<FinalizeWeek> logger)
    {
        [Function("FinalizeWeek")]
        public async Task Run([TimerTrigger("0 0 12 * * 2")] TimerInfo timer, CancellationToken ct) //run at noon Tuesday UTC - all NFL games should be wrapped up
        {
            var leagueId = league.Value.CurrentLeagueId;
            var snapshot = await seasons.BuildAsync(leagueId, ct);
            var finalized = snapshot.Teams.SelectMany(t => t.Weeks).Where(w => w.IsFinal).ToList();

            await storedWeeks.WriteAsync(leagueId, snapshot.Season, finalized, ct);

            logger.LogInformation("Finalized {WeekCount} team-weeks through week {FinalThroughWeek} of {Season}. Next: {Next}.",
                finalized.Count, snapshot.FinalThroughWeek, snapshot.Season, timer.ScheduleStatus?.Next);
        }
    }
}
