using System.Diagnostics;
using FantasyLeague.Core;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Assembles a whole season: matchups joined to the player map, solved, and
    /// grouped per roster.
    /// </summary>
    /// <remarks>The public seam. The implementation and its dependencies stay internal.</remarks>
    public interface ISeasonService
    {
        /// <summary>
        /// Builds a season snapshot for one league.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>Every roster's weeks, plus whatever did not add up.</returns>
        Task<SeasonSnapshot> BuildAsync(string leagueId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Assembles a whole season: matchups joined to the player map, solved, and
    /// grouped per roster.
    /// </summary>
    /// <remarks>
    /// Takes the league as an argument rather than reading configuration. D8's
    /// chain walk builds prior seasons from different league ids, and the console
    /// harness is handed one on the command line; only the edges read config.
    /// </remarks>
    internal sealed class SeasonService(
        SleeperReaderCache reader,
        IPlayerMapRefresher players,
        ILogger<SeasonService> logger) : ISeasonService
    {
        /// <summary>Float drift tolerance for the starters-sum invariant.</summary>
        private const decimal Cent = 0.01m;

        /// <summary>
        /// Builds a season snapshot for one league.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>Every roster's weeks, plus whatever did not add up.</returns>
        /// <exception cref="InvalidOperationException">The player map has never been refreshed.</exception>
        public async Task<SeasonSnapshot> BuildAsync(string leagueId, CancellationToken cancellationToken)
        {
            await players.EnsureCurrentAsync(cancellationToken);

            // the map starts empty; without this every player comes back unresolved
            if (players.LastRefreshedUtc is null)
            {
                throw new InvalidOperationException("The player map has not been refreshed.");
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            logger.LogInformation("Begin building season snapshot.");
            var state = await reader.GetStateAsync(cancellationToken);

            var league = await reader.GetLeagueAsync(leagueId, cancellationToken);

            var slots = Slot.Parse(league!.RosterPositions!);
            var users = await reader.GetUsersAsync(leagueId, cancellationToken);
            var rosters = await reader.GetRostersAsync(leagueId, cancellationToken);
            var names = SleeperMapper.ToTeamNames(users!, rosters!);
            var owners = rosters!
                .Where(r => r!.OwnerId is not null)
                .ToDictionary(r => r!.RosterId, r => r!.OwnerId!);
            var violations = new List<InvariantViolation>();
            var weeks = new List<TeamWeek>();
            var unresolved = new List<string>();
            for (int week = 1; week <= state!.Week; week++)
            {
                var isFinal = IsFinal(week, state!.Week);
                var (weekTeamWeeks, weekUnresolved, weekViolations) = await BuildWeekAsync(leagueId, week, slots, isFinal, cancellationToken);
                weeks.AddRange(weekTeamWeeks);
                unresolved.AddRange(weekUnresolved);
                violations.AddRange(weekViolations);
            }
            //group teams by week and rosterId, then sort by rosterId and week, then build TeamSeason objects
            var teams = weeks
                .GroupBy(w => w.RosterId)
                .Select(g => new TeamSeason(
                    g.Key,
                    names.TryGetValue(g.Key, out var name) ? name : $"Roster {g.Key}",
                    g.OrderBy(w => w.Week).ToList()))
                .OrderBy(t => t.RosterId)
                .ToList();

            logger.LogInformation("Finished building season snapshot in {ElapsedMilliseconds}ms.", stopwatch.ElapsedMilliseconds);

            return new SeasonSnapshot(
                state.Season,
                state.Week,
                Math.Max(0, state.Week - 1),
                slots,
                teams,
                unresolved.Distinct().ToList(),
                violations,
                owners);
        }

        /// <summary>
        /// Builds one week for every roster in the league.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="week">Week</param>
        /// <param name="slots">Startable slots for this season.</param>
        /// <param name="isFinal">Whether the week's scores are settled.</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>One TeamWeek per roster, plus anything that did not add up.</returns>
        private async Task<(IReadOnlyList<TeamWeek> Weeks, IReadOnlyList<string> Unresolved, IReadOnlyList<InvariantViolation> Violations)>
            BuildWeekAsync(
                string leagueId,
                int week,
                IReadOnlyList<Slot> slots,
                bool isFinal,
                CancellationToken cancellationToken)
        {
            var matchups = await reader.GetMatchupsAsync(leagueId, week, cancellationToken);
            var teamWeeks = new List<TeamWeek>();
            var unresolved = new List<string>();
            var violations = new List<InvariantViolation>();
            foreach (var matchup in matchups!)
            {
                var mapped = SleeperMapper.ToRosterWeek(matchup!, players.PlayerMap);
                (var idealPoints, var idealRoster) = IdealLineupSolver.Solve(mapped.Players, slots);
                teamWeeks.Add(new TeamWeek(mapped.RosterId, week, mapped.Actual, idealPoints, mapped.Starters, idealRoster, isFinal));
                unresolved.AddRange(mapped.UnresolvedPlayerIds);
                violations.AddRange(Check(mapped, idealPoints, week));
            }

            return (teamWeeks, unresolved, violations);
        }

        /// <summary>
        /// Checks one roster-week against both invariants.
        /// </summary>
        /// <param name="mapped">The mapped week.</param>
        /// <param name="ideal">Ideal points from the solver.</param>
        /// <param name="week">Week</param>
        /// <returns>Whatever failed, or empty.</returns>
        private static IReadOnlyList<InvariantViolation> Check(RosterWeek mapped, decimal ideal, int week)
        {
            var violations = new List<InvariantViolation>();
            var startersSum = mapped.StartersPoints.Sum();

            if (Math.Abs(startersSum - mapped.Points) > Cent)
            {
                violations.Add(new InvariantViolation(week, mapped.RosterId, InvariantKind.StartersSumMatchesPoints, mapped.Points, startersSum));
            }

            if (ideal < mapped.Points - Cent)
            {
                violations.Add(new InvariantViolation(week, mapped.RosterId, InvariantKind.IdealNotBelowActual, mapped.Points, ideal));
            }

            return violations;
        }

        /// <summary>
        /// Decides whether a week's scores are settled.
        /// </summary>
        /// <param name="week">Week</param>
        /// <param name="currentWeek">The week Sleeper considers current.</param>
        /// <returns>True when the week is done.</returns>
        /// <remarks>
        /// Approximated from state until M3: T28's finalize timer stamps IsFinal in
        /// SQL and this reads it instead. Kept in one place so that swap is local.
        /// </remarks>
        private static bool IsFinal(int week, int currentWeek) => week < currentWeek;
    }
}
