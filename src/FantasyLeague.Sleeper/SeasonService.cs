using System.Diagnostics;
using FantasyLeague.Core;
using Microsoft.Extensions.Caching.Hybrid;
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

        /// <summary>
        /// Gets the season Sleeper considers current, without building it.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>The current season's year.</returns>
        Task<int> GetCurrentSeasonAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Assembles a whole season: matchups joined to the player map, solved, and
    /// grouped per roster.
    /// </summary>
    internal sealed class SeasonService(
        SleeperReaderCache reader,
        IPlayerMapRefresher players,
        IFinalizedWeekStore stored,
        HybridCache cache,
        ILogger<SeasonService> logger) : ISeasonService
    {
        /// <summary>Float drift tolerance for the starters-sum invariant.</summary>
        private const decimal Cent = 0.01m;

        /// <summary>
        /// As long as the live response's max-age, so every poll inside it shares one build.
        /// </summary>
        private static readonly HybridCacheEntryOptions Snapshot = new() { Expiration = TimeSpan.FromSeconds(5) };

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">The player map has never been refreshed.</exception>
        public async Task<SeasonSnapshot> BuildAsync(string leagueId, CancellationToken cancellationToken) =>
            await cache.GetOrCreateAsync(
                $"season:{leagueId}",
                async ct =>
                {
                    var snapshot = await BuildUncachedAsync(leagueId, ct);
                    Report(snapshot);
                    return snapshot;
                },
                Snapshot,
                cancellationToken: cancellationToken);

        /// <inheritdoc />
        public async Task<int> GetCurrentSeasonAsync(CancellationToken cancellationToken) =>
            (await reader.GetStateAsync(cancellationToken)).Season;

        /// <summary>
        /// Assembles the season from Sleeper and the store.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>Every roster's weeks, plus whatever did not add up.</returns>
        private async Task<SeasonSnapshot> BuildUncachedAsync(string leagueId, CancellationToken cancellationToken)
        {
            await players.EnsureCurrentAsync(cancellationToken);

            // the map starts empty; without this every player comes back unresolved
            if (players.LastRefreshedUtc is null)
            {
                throw new InvalidOperationException("The player map has not been refreshed.");
            }
            var stopwatch = Stopwatch.StartNew();
            logger.LogInformation("Begin building season snapshot.");
            var state = await reader.GetStateAsync(cancellationToken);

            var league = await reader.GetLeagueAsync(leagueId, cancellationToken);

            var name = SleeperMapper.ToLeagueName(league);
            var slots = SleeperMapper.ToSlots(league);
            var users = await reader.GetUsersAsync(leagueId, cancellationToken);
            var rosters = await reader.GetRostersAsync(leagueId, cancellationToken);
            var names = SleeperMapper.ToTeamNames(users, rosters);
            var owners = SleeperMapper.ToRosterOwnerIds(rosters);
            var violations = new List<InvariantViolation>();
            var weeks = new List<TeamWeek>();
            var unresolved = new List<string>();
            var storedWeeks = await stored.ReadAsync(leagueId, state.Season, cancellationToken);
            var storedByWeek = storedWeeks.ToLookup(w => w.Week);
            for (var week = 1; week <= state.Week; week++)
            {
                var isFinal = WeekFinality.IsFinal(week, state.Week);

                if (isFinal && storedByWeek.Contains(week))
                {
                    weeks.AddRange(storedByWeek[week]);
                    continue;
                }

                var (weekTeamWeeks, weekUnresolved, weekViolations) = await BuildWeekAsync(leagueId, week, slots, isFinal, cancellationToken);
                weeks.AddRange(weekTeamWeeks);
                unresolved.AddRange(weekUnresolved);
                violations.AddRange(weekViolations);
            }
            //group teams by week and rosterId, then sort by rosterId and week, then build TeamSeason objects
            var teams = weeks.GroupBy(w => w.RosterId).Select(g => new TeamSeason(g.Key, names.TryGetValue(g.Key, out var name) ? name : $"Roster {g.Key}", g.OrderBy(w => w.Week).ToList()))
                .OrderBy(t => t.RosterId)
                .ToList();

            logger.LogInformation("Finished building season snapshot in {ElapsedMilliseconds}ms.", stopwatch.ElapsedMilliseconds);

            return new SeasonSnapshot(name, state.Season, state.Week, WeekFinality.FinalThroughWeek(state.Week), slots, teams, unresolved.Distinct().ToList(), violations, owners);
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
            foreach (var matchup in matchups)
            {
                var mapped = SleeperMapper.ToRosterWeek(matchup, players.PlayerMap);
                (var idealPoints, var idealRoster) = IdealLineupSolver.Solve(mapped.Players, slots);

                var positions = mapped.Players.ToDictionary(p => p.Id.Value, p => p.FantasyPositions);
                var slotAverages = SlotAverager.MakeSlotAverages(
                    SleeperMapper.ToSlotStarters(matchup), matchup.PlayersPoints ?? new Dictionary<string, decimal>(),
                    positions, slots);

                if (slotAverages is null)
                {
                    // roster_positions changed after this week was played
                    logger.LogError(
                        "Roster {RosterId} week {Week}: starters do not line up with the startable slots; excluded from radar attribution.",
                        mapped.RosterId, week);
                }

                teamWeeks.Add(new TeamWeek(mapped.RosterId, week, mapped.Actual, idealPoints, mapped.Starters, idealRoster, mapped.Players, isFinal, slotAverages ?? new Dictionary<string, decimal>()));
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
        /// Records every invariant breach and unresolved player, once per build
        /// rather than once per request that reads it.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        private void Report(SeasonSnapshot snapshot)
        {
            foreach (var violation in snapshot.Violations)
            {
                logger.LogError(
                    "Invariant {Invariant} failed for roster {RosterId} in week {Week}: expected {Expected}, got {Actual}.",
                    violation.Invariant, violation.RosterId, violation.Week, violation.Expected, violation.Actual);
            }

            foreach (var playerId in snapshot.UnresolvedPlayerIds)
            {
                logger.LogWarning("Rostered player {PlayerId} is missing from the player map.", playerId);
            }
        }
    }
}
