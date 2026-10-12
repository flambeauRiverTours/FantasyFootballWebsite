using FantasyLeague.Core;
using FantasyLeague.Sleeper;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// Assembles a whole season: matchups joined to the player map, solved, and
    /// grouped per roster.
    /// </summary>
    /// <remarks>The public seam. The implementation and its dependencies stay internal.</remarks>
    public interface ISeasonService
    {
        /// <summary>
        /// Builds the configured league's current season, with the league's
        /// name override and exclusions applied.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>Every roster's weeks, plus whatever did not add up.</returns>
        Task<SeasonSnapshot> BuildAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Gets the season Sleeper considers current, without building it.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The current season's year.</returns>
        Task<int> GetCurrentSeasonAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Builds finalized weeks from the store and every other week from Sleeper,
    /// once per cache window.
    /// </summary>
    /// <param name="sleeper">League, weeks and NFL state from Sleeper.</param>
    /// <param name="players">The player map the weeks are joined to.</param>
    /// <param name="stored">Weeks already finalized.</param>
    /// <param name="cache">Holds a built season for the cache window.</param>
    /// <param name="options">The configured league and its local rules.</param>
    /// <param name="time">Clock for the build's duration.</param>
    /// <param name="logger">Logger.</param>
    internal sealed class SeasonService(
        ISleeperReader sleeper,
        IPlayerMapRefresher players,
        IFinalizedWeekStore stored,
        HybridCache cache,
        IOptions<LeagueOptions> options,
        TimeProvider time,
        ILogger<SeasonService> logger) : ISeasonService
    {
        /// <summary>Tolerance for both invariants: Sleeper reports points to the cent.</summary>
        private const decimal Cent = 0.01m;

        /// <summary>
        /// As long as the live response's max-age, so every poll inside it shares one build.
        /// </summary>
        private static readonly HybridCacheEntryOptions Snapshot = new() { Expiration = TimeSpan.FromSeconds(5) };

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">The player map has never been refreshed.</exception>
        public async Task<SeasonSnapshot> BuildAsync(CancellationToken cancellationToken) =>
            await cache.GetOrCreateAsync(
                $"season:{options.Value.CurrentLeagueId}",
                async token =>
                {
                    var snapshot = await BuildUncachedAsync(options.Value, token);
                    Report(snapshot);
                    return snapshot;
                },
                Snapshot,
                cancellationToken: cancellationToken);

        /// <inheritdoc />
        public async Task<int> GetCurrentSeasonAsync(CancellationToken cancellationToken) =>
            (await sleeper.GetNFLStateAsync(cancellationToken)).Season;

        /// <summary>
        /// Assembles the season from Sleeper and the store.
        /// </summary>
        /// <param name="configured">The league and its local rules.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>Every roster's weeks, plus whatever did not add up.</returns>
        private async Task<SeasonSnapshot> BuildUncachedAsync(LeagueOptions configured, CancellationToken cancellationToken)
        {
            var leagueId = configured.CurrentLeagueId;
            await players.EnsureCurrentAsync(cancellationToken);

            // the map starts empty; without this every player comes back unresolved
            if (players.LastRefreshedUtc is null)
            {
                throw new InvalidOperationException("The player map has not been refreshed.");
            }
            var started = time.GetTimestamp();
            logger.LogInformation("Begin building season snapshot.");
            var state = await sleeper.GetNFLStateAsync(cancellationToken);
            var league = await sleeper.GetLeagueAsync(leagueId, cancellationToken);

            var leagueName = string.IsNullOrWhiteSpace(configured.OverrideLeagueName)
                ? league.Name
                : configured.OverrideLeagueName;
            var excluded = ExcludedRosters.Resolve(league.RosterOwnerIds, configured.ExcludedOwnerIds);
            if (excluded.UnmatchedOwnerIds.Count > 0)
            {
                // the ids themselves are Sleeper user ids, which are never logged (A2)
                logger.LogWarning(
                    "{UnmatchedCount} excluded owner id(s) match no roster in season {Season}; those teams are still counted in every metric.",
                    excluded.UnmatchedOwnerIds.Count,
                    state.Season);
            }

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

                var (weekTeamWeeks, weekUnresolved, weekViolations) = await BuildWeekAsync(leagueId, week, league.Slots, isFinal, cancellationToken);
                weeks.AddRange(weekTeamWeeks);
                unresolved.AddRange(weekUnresolved);
                violations.AddRange(weekViolations);
            }

            var teams = weeks
                .GroupBy(w => w.RosterId)
                .OrderBy(g => g.Key)
                .Select(g => new TeamSeason(
                    g.Key,
                    league.TeamNames.GetValueOrDefault(g.Key, $"Roster {g.Key}"),
                    [.. g.OrderBy(w => w.Week)]))
                .ToList();

            logger.LogInformation("Finished building season snapshot in {ElapsedMilliseconds}ms.", (long)time.GetElapsedTime(started).TotalMilliseconds);

            return new SeasonSnapshot(
                leagueName,
                state.Season,
                state.Week,
                WeekFinality.FinalThroughWeek(state.Week),
                league.Slots,
                teams,
                [.. unresolved.Distinct()],
                violations,
                excluded.RosterIds);
        }

        /// <summary>
        /// Builds one week for every roster in the league.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="week">The week.</param>
        /// <param name="slots">Startable slots for this season.</param>
        /// <param name="isFinal">Whether the week's scores are settled.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>One TeamWeek per roster, plus anything that did not add up.</returns>
        private async Task<(IReadOnlyList<TeamWeek> Weeks, IReadOnlyList<string> Unresolved, IReadOnlyList<InvariantViolation> Violations)>
            BuildWeekAsync(
                string leagueId,
                int week,
                IReadOnlyList<Slot> slots,
                bool isFinal,
                CancellationToken cancellationToken)
        {
            var rosterWeeks = await sleeper.GetWeekAsync(leagueId, week, players.PlayerMap, cancellationToken);
            var teamWeeks = new List<TeamWeek>();
            var unresolved = new List<string>();
            var violations = new List<InvariantViolation>();
            foreach (var mapped in rosterWeeks)
            {
                (var idealPts, var idealLineup) = IdealLineupSolver.Solve(mapped.Players, slots);

                var positions = mapped.Players.ToDictionary(p => p.Id.Value, p => p.FantasyPositions);
                var slotAverages = SlotAverager.WeekAverages(mapped.SlotStarters, mapped.PlayersPoints, positions, slots);

                if (slotAverages is null)
                {
                    // roster_positions changed after this week was played
                    logger.LogError(
                        "Roster {RosterId} week {Week}: starters do not line up with the startable slots; excluded from radar attribution.",
                        mapped.RosterId, week);
                }

                teamWeeks.Add(new TeamWeek(
                    mapped.RosterId,
                    week,
                    mapped.ActualPts,
                    idealPts,
                    mapped.Starters,
                    idealLineup,
                    mapped.Players,
                    isFinal,
                    slotAverages ?? new Dictionary<string, decimal>()));
                unresolved.AddRange(mapped.UnresolvedPlayerIds);
                violations.AddRange(Check(mapped, idealPts, week));
            }

            return (teamWeeks, unresolved, violations);
        }

        /// <summary>
        /// Checks one roster-week against both invariants.
        /// </summary>
        /// <param name="mapped">The mapped week.</param>
        /// <param name="idealPts">Ideal points from the solver.</param>
        /// <param name="week">The week.</param>
        /// <returns>Whatever failed, or empty.</returns>
        private static IReadOnlyList<InvariantViolation> Check(RosterWeek mapped, decimal idealPts, int week)
        {
            var violations = new List<InvariantViolation>();
            var startersSum = mapped.StartersPoints.Sum();

            if (Math.Abs(startersSum - mapped.ScoredPts) > Cent)
            {
                violations.Add(new InvariantViolation(week, mapped.RosterId, InvariantKind.StartersSumMatchesPoints, mapped.ScoredPts, startersSum));
            }

            if (idealPts < mapped.ScoredPts - Cent)
            {
                violations.Add(new InvariantViolation(week, mapped.RosterId, InvariantKind.IdealNotBelowActual, mapped.ScoredPts, idealPts));
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
