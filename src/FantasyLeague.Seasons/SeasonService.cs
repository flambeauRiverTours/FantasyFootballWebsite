using System.ComponentModel;
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
        /// <returns>Every roster's weeks, plus whatever did not add up.</returns>
        Task<SeasonSnapshot> BuildAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Gets the season Sleeper considers current, without building it.
        /// </summary>
        /// <returns>The current season's year.</returns>
        Task<int> GetCurrentSeasonAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Builds a completed season from its stored weeks and its own league's names and slots.
        /// </summary>
        /// <param name="season">The year.</param>
        /// <returns>The season, or null when the chain does not reach it or nothing is stored for it.</returns>
        Task<SeasonSnapshot?> BuildPastAsync(int season, CancellationToken cancellationToken);

        /// <summary>
        /// Gets every season that can be served: the current one, and each earlier one with stored weeks.
        /// </summary>
        /// <returns>Years, newest first.</returns>
        Task<IReadOnlyList<int>> GetAvailableSeasonsAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// What reading a season's weeks produced, gathered as the weeks are read.
    /// </summary>
    internal sealed class SeasonWeeks
    {
        /// <summary>Gets every roster's weeks, excluded rosters included.</summary>
        public List<TeamWeek> TeamWeeks { get; } = [];

        /// <summary>Gets every rostered id the player map could not explain.</summary>
        public List<string> Unresolved { get; } = [];

        /// <summary>Gets every invariant breach.</summary>
        public List<InvariantViolation> Violations { get; } = [];
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
    /// <param name="reported">What this instance has already logged.</param>
    /// <param name="chain">League ids of earlier seasons (D8).</param>
    internal sealed class SeasonService(
        ISleeperReader sleeper,
        IPlayerMapRefresher players,
        IFinalizedWeekStore stored,
        HybridCache cache,
        IOptions<LeagueOptions> options,
        TimeProvider time,
        ReportedProblems reported,
        ISeasonChain chain,
        ILogger<SeasonService> logger) : ISeasonService
    {
        /// <summary>Tolerance for both invariants: Sleeper reports points to the cent.</summary>
        private const decimal Cent = 0.01m;

        /// <summary>
        /// As long as the live response's max-age, so every poll inside it shares one build.
        /// </summary>
        private static readonly HybridCacheEntryOptions Snapshot = new() { Expiration = TimeSpan.FromSeconds(5) };

        /// <summary>A completed season changes only if its weeks are backfilled; a day bounds that delay.</summary>
        private static readonly HybridCacheEntryOptions PastSeason = new() { Expiration = TimeSpan.FromHours(24) };

        /// <inheritdoc />
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

        /// <inheritdoc />
        public async Task<SeasonSnapshot?> BuildPastAsync(int season, CancellationToken cancellationToken) =>
            await cache.GetOrCreateAsync(
                $"past:{season}",
                async token => new PastBuild(await BuildPastUncachedAsync(season, options.Value, token)),
                PastSeason,
                cancellationToken: cancellationToken) is { Snapshot: var snapshot } ? snapshot : null;

        /// <inheritdoc />
        public async Task<IReadOnlyList<int>> GetAvailableSeasonsAsync(CancellationToken cancellationToken)
        {
            var current = await GetCurrentSeasonAsync(cancellationToken);
            var available = new List<int> { current };

            foreach (var (season, leagueId) in await chain.GetAsync(cancellationToken))
            {
                if (season != current && (await stored.ReadAsync(leagueId, season, cancellationToken)).Count > 0)
                {
                    available.Add(season);
                }
            }

            return [.. available.OrderDescending()];
        }

        /// <summary>
        /// Assembles a completed season: every week from the store, final, with no Sleeper matchups.
        /// </summary>
        /// <param name="season">The year.</param>
        /// <param name="configured">The league's local rules.</param>
        /// <returns>The season, or null when it cannot be served.</returns>
        private async Task<SeasonSnapshot?> BuildPastUncachedAsync(int season, LeagueOptions configured, CancellationToken cancellationToken)
        {
            if (!(await chain.GetAsync(cancellationToken)).TryGetValue(season, out var leagueId))
            {
                return null;
            }

            var weeks = await stored.ReadAsync(leagueId, season, cancellationToken);
            if (weeks.Count == 0)
            {
                return null;
            }

            var league = await sleeper.GetLeagueAsync(leagueId, cancellationToken);
            var excluded = ResolveExclusions(league, configured, season);
            var lastWeek = weeks.Max(w => w.Week);

            return new SeasonSnapshot(
                string.IsNullOrWhiteSpace(configured.OverrideLeagueName) ? league.Name : configured.OverrideLeagueName,
                season,
                lastWeek,
                lastWeek,
                league.Slots,
                GroupTeams(weeks, league.Teams, excluded),
                [],
                [],
                IsCurrent: false);
        }

        /// <summary>
        /// Assembles the season from Sleeper and the store.
        /// </summary>
        /// <param name="configured">The league and its local rules.</param>
        /// <returns>Every roster's weeks, plus whatever did not add up.</returns>
        private async Task<SeasonSnapshot> BuildUncachedAsync(LeagueOptions configured, CancellationToken cancellationToken)
        {
            var playerMap = await players.EnsureCurrentAsync(cancellationToken);
            var started = time.GetTimestamp();

            var state = await sleeper.GetNFLStateAsync(cancellationToken);
            var league = await sleeper.GetLeagueAsync(configured.CurrentLeagueId, cancellationToken);
            var excluded = ResolveExclusions(league, configured, state.Season);
            var weeks = await ReadWeeksAsync(configured.CurrentLeagueId, state, league.Slots, playerMap, excluded, cancellationToken);

            // debug: under load this runs every 5 s, and App Insights already times each request
            logger.LogDebug("Built season snapshot in {ElapsedMilliseconds}ms.", (long)time.GetElapsedTime(started).TotalMilliseconds);

            return new SeasonSnapshot(
                string.IsNullOrWhiteSpace(configured.OverrideLeagueName) ? league.Name : configured.OverrideLeagueName,
                state.Season,
                state.Week,
                WeekFinality.FinalThroughWeek(state.Week),
                league.Slots,
                GroupTeams(weeks.TeamWeeks, league.Teams, excluded),
                [.. weeks.Unresolved.Distinct()],
                weeks.Violations,
                IsCurrent: true);
        }

        /// <summary>
        /// Resolves the configured owner ids to this season's rosters (A1).
        /// </summary>
        /// <param name="league">The league season.</param>
        /// <param name="configured">The configured exclusions.</param>
        /// <param name="season">The season, for the warning.</param>
        /// <returns>The rosters to leave out of every metric.</returns>
        private IReadOnlySet<int> ResolveExclusions(SleeperLeague league, LeagueOptions configured, int season)
        {
            var excluded = ExcludedRosters.Resolve(league.Teams, configured.ExcludedOwnerIds);
            if (excluded.UnmatchedOwnerIds.Count > 0)
            {
                // the ids themselves are Sleeper user ids, which are never logged (A2)
                logger.LogWarning(
                    "{UnmatchedCount} excluded owner id(s) match no roster in season {Season}; those teams are still counted in every metric.",
                    excluded.UnmatchedOwnerIds.Count,
                    season);
            }

            return excluded.RosterIds;
        }

        /// <summary>
        /// Reads every week so far: finalized ones from the store, the rest from Sleeper.
        /// </summary>
        /// <param name="state">The current season and week.</param>
        /// <param name="slots">Startable slots for this season.</param>
        /// <param name="playerMap">The player map Sleeper's weeks are joined to.</param>
        /// <param name="excluded">Rosters left out of every metric (A1), solved and checked all the same.</param>
        /// <returns>Every roster's weeks, with what did not resolve or add up.</returns>
        private async Task<SeasonWeeks> ReadWeeksAsync(
            string leagueId,
            NFLState state,
            IReadOnlyList<Slot> slots,
            IReadOnlyDictionary<string, PlayerRef> playerMap,
            IReadOnlySet<int> excluded,
            CancellationToken cancellationToken)
        {
            var weeks = new SeasonWeeks();
            var storedByWeek = (await stored.ReadAsync(leagueId, state.Season, cancellationToken)).ToLookup(w => w.Week);

            for (var week = 1; week <= state.Week; week++)
            {
                var isFinal = WeekFinality.IsFinal(week, state.Week);
                if (isFinal && storedByWeek.Contains(week))
                {
                    weeks.TeamWeeks.AddRange(storedByWeek[week]);
                    continue;
                }

                foreach (var mapped in await sleeper.GetWeekAsync(leagueId, week, playerMap, cancellationToken))
                {
                    var teamWeek = ToTeamWeek(mapped, week, slots, isFinal, excluded.Contains(mapped.RosterId));
                    weeks.TeamWeeks.Add(teamWeek);
                    weeks.Unresolved.AddRange(mapped.UnresolvedPlayerIds);
                    weeks.Violations.AddRange(Check(mapped, teamWeek.IdealPts, week));
                }
            }

            return weeks;
        }

        /// <summary>
        /// Solves one roster's week and attributes its points to slots.
        /// </summary>
        /// <param name="mapped">The roster's week from Sleeper.</param>
        /// <param name="slots">Startable slots for this season.</param>
        /// <param name="isFinal">Whether the week's scores are settled.</param>
        /// <param name="isExcluded">Whether the roster is left out of every metric (A1).</param>
        /// <returns>The week as the domain holds it.</returns>
        private TeamWeek ToTeamWeek(RosterWeek mapped, int week, IReadOnlyList<Slot> slots, bool isFinal, bool isExcluded)
        {
            var (idealPts, idealLineup) = IdealLineupSolver.Solve(mapped.Players, slots);

            var positions = mapped.Players.ToDictionary(p => p.Id.Value, p => p.FantasyPositions);
            var slotAverages = SlotAverager.WeekAverages(mapped.SlotStarters, mapped.PlayersPoints, positions, slots);
            // the placeholder team's lineup is never set, so it fails this every live week;
            // it never reaches the radar, so there is nothing to report
            if (slotAverages is null && !isExcluded)
            {
                // roster_positions changed after this week was played
                logger.LogError(
                    "Roster {RosterId} week {Week}: starters do not line up with the startable slots; excluded from radar attribution.",
                    mapped.RosterId, week);
            }

            return new TeamWeek(
                mapped.RosterId,
                week,
                mapped.ActualPts,
                idealPts,
                mapped.Starters,
                idealLineup,
                mapped.Players,
                isFinal,
                slotAverages ?? new Dictionary<string, decimal>());
        }

        /// <summary>
        /// Groups the weeks into one season per roster, leaving out excluded rosters.
        /// </summary>
        /// <param name="weeks">Every roster's weeks.</param>
        /// <param name="teams">The league's teams, for their names.</param>
        /// <param name="excluded">Rosters left out of every metric (A1).</param>
        /// <returns>One season per counted roster, in roster order.</returns>
        /// <remarks>
        /// A1 is applied here, so nothing downstream of the snapshot can count
        /// an excluded roster. Both invariants have already checked it.
        /// </remarks>
        private static IReadOnlyList<TeamSeason> GroupTeams(
            IEnumerable<TeamWeek> weeks,
            IReadOnlyDictionary<int, SleeperTeam> teams,
            IReadOnlySet<int> excluded) =>
            [.. weeks
                .Where(w => !excluded.Contains(w.RosterId))
                .GroupBy(w => w.RosterId)
                .OrderBy(g => g.Key)
                .Select(g => new TeamSeason(
                    g.Key,
                    teams.TryGetValue(g.Key, out var team) ? team.Name : SleeperTeam.FallbackName(g.Key),
                    [.. g.OrderBy(w => w.Week)]))];

        /// <summary>
        /// Checks one roster-week against both invariants.
        /// </summary>
        /// <param name="mapped">The mapped week.</param>
        /// <param name="idealPts">Ideal points from the solver.</param>
        /// <returns>Whatever failed, or empty.</returns>
        private static IReadOnlyList<InvariantViolation> Check(RosterWeek mapped, decimal idealPts, int week)
        {
            var violations = new List<InvariantViolation>();
            var startersSum = mapped.StartersPoints.Sum();

            if (Math.Abs(startersSum - mapped.SleeperPts) > Cent)
            {
                violations.Add(new InvariantViolation(week, mapped.RosterId, InvariantKind.StartersSumMatchesPoints, mapped.SleeperPts, startersSum));
            }

            if (idealPts < mapped.SleeperPts - Cent)
            {
                violations.Add(new InvariantViolation(week, mapped.RosterId, InvariantKind.IdealNotBelowActual, mapped.SleeperPts, idealPts));
            }

            return violations;
        }

        /// <summary>
        /// Logs each invariant breach and unresolved player the first time this
        /// instance sees it, rather than on every build.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        private void Report(SeasonSnapshot snapshot)
        {
            foreach (var violation in snapshot.Violations.Where(v => reported.IsNew($"invariant:{v.Invariant}:{v.RosterId}:{v.Week}")))
            {
                logger.LogError(
                    "Invariant {Invariant} failed for roster {RosterId} in week {Week}: Sleeper scored {SleeperPts}, compared against {ComparedPts}.",
                    violation.Invariant, violation.RosterId, violation.Week, violation.SleeperPts, violation.ComparedPts);
            }

            foreach (var playerId in snapshot.UnresolvedPlayerIds.Where(id => reported.IsNew($"player:{id}")))
            {
                logger.LogWarning("Rostered player {PlayerId} is missing from the player map.", playerId);
            }
        }

        /// <summary>
        /// A past season's build, null when it cannot be served, wrapped so the
        /// cache holds the answer either way and hands back one instance.
        /// </summary>
        /// <param name="Snapshot">The season, or null.</param>
        [ImmutableObject(true)]
        private sealed record PastBuild(SeasonSnapshot? Snapshot);
    }
}
