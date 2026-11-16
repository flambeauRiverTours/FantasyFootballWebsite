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

        /// <summary>
        /// Fills in the game of every stored week read before matchups were, and stores the
        /// excluded rosters' missing weeks, for every season the chain reaches (T68).
        /// </summary>
        /// <returns>How many weeks were read from Sleeper.</returns>
        /// <remarks>
        /// Reads at most <see cref="SeasonService.BackfillWeeksPerRun"/> weeks a run, so the
        /// live site keeps the token bucket; it finishes over a few runs, after which a run
        /// reads the store and the cached chain and calls Sleeper for nothing else. A
        /// season that fails is logged and the others carry on.
        /// </remarks>
        Task<int> BackfillMatchupsAsync(CancellationToken cancellationToken);
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

        /// <summary>Gets or sets the week the season was built to: Sleeper's, unless it went back past stored weeks.</summary>
        public int CurrentWeek { get; set; }
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
        /// <summary>
        /// The most weeks one backfill run reads: about half a minute of the token
        /// bucket, so the live site is never kept waiting (T68).
        /// </summary>
        internal const int BackfillWeeksPerRun = 12;

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

            return Assemble(league, configured, excluded, weeks, season, lastWeek, lastWeek, isCurrent: false);
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
            if (state.SeasonType is { } seasonType && reported.IsNew($"season-type:{state.Season}:{seasonType}"))
            {
                // A37: what the state does after the NFL regular season is unobserved; this records it
                logger.LogInformation(
                    "Sleeper's NFL state for {Season} is {SeasonType} at week {Week}.", state.Season, seasonType, state.Week);
            }

            var league = await sleeper.GetLeagueAsync(configured.CurrentLeagueId, cancellationToken);
            var excluded = ResolveExclusions(league, configured, state.Season);
            var weeks = await ReadWeeksAsync(configured.CurrentLeagueId, state, league.Slots, playerMap, excluded, cancellationToken);

            // debug: under load this runs every 5 s, and App Insights already times each request
            logger.LogDebug("Built season snapshot in {ElapsedMilliseconds}ms.", (long)time.GetElapsedTime(started).TotalMilliseconds);

            var snapshot = Assemble(
                league, configured, excluded, weeks.TeamWeeks, state.Season,
                weeks.CurrentWeek, WeekFinality.FinalThroughWeek(weeks.CurrentWeek), isCurrent: true) with
            {
                UnresolvedPlayerIds = [.. weeks.Unresolved.Distinct()],
                Violations = weeks.Violations,
            };

            return snapshot with { RecordMismatches = RecordMismatch.Find(snapshot.Records, league.Teams) };
        }

        /// <summary>
        /// Builds a season's snapshot, the excluded rosters kept apart (A1), with nothing yet to report.
        /// </summary>
        /// <param name="league">That season's league: its name, slots, rosters and rules.</param>
        /// <param name="configured">The name override and the format.</param>
        /// <param name="excluded">The rosters A1 leaves out of every metric.</param>
        /// <param name="weeks">Every roster's weeks.</param>
        /// <param name="season">The year.</param>
        /// <param name="currentWeek">The week Sleeper considers current, or a past season's last.</param>
        /// <param name="finalThroughWeek">The last settled week.</param>
        /// <param name="isCurrent">Whether this is the live season (A34).</param>
        /// <returns>The snapshot.</returns>
        private static SeasonSnapshot Assemble(
            SleeperLeague league,
            LeagueOptions configured,
            IReadOnlySet<int> excluded,
            IReadOnlyList<TeamWeek> weeks,
            int season,
            int currentWeek,
            int finalThroughWeek,
            bool isCurrent) =>
            new(
                string.IsNullOrWhiteSpace(configured.OverrideLeagueName) ? league.Name : configured.OverrideLeagueName,
                season,
                currentWeek,
                finalThroughWeek,
                league.Slots,
                GroupTeams(weeks, league.Teams, id => !excluded.Contains(id)),
                [],
                [],
                isCurrent,
                RegularSeason.LastWeek(league.PlayoffWeekStart),
                configured.Format,
                GroupTeams(weeks, league.Teams, excluded.Contains),
                league.HasMedianGame,
                []);

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
            var storedWeeks = await stored.ReadAsync(leagueId, state.Season, cancellationToken);
            var storedByWeek = storedWeeks.ToLookup(w => w.Week);
            var storedThrough = storedWeeks.Count == 0 ? 0 : storedWeeks.Max(w => w.Week);

            // only final weeks are stored, so in a season that moves forward Sleeper's week is always
            // past them; a week behind them means the state reset, and the stored season stands.
            // Equal is not a reset: the live week is read from Sleeper whatever the store holds for it
            var wentBack = state.Week < storedThrough;
            var lastRead = Math.Max(state.Week, storedThrough);
            // a reset comes after the NFL's regular season, so every week to 18 has settled, stored or not
            weeks.CurrentWeek = wentBack ? Math.Max(storedThrough, RegularSeason.NFLWeeks) + 1 : state.Week;
            if (wentBack && reported.IsNew($"week-back:{state.Season}:{state.Week}"))
            {
                logger.LogWarning(
                    "Sleeper reports week {Week} ({SeasonType}) of {Season}, but weeks through {StoredThrough} are stored; showing the stored season.",
                    state.Week, state.SeasonType, state.Season, storedThrough);
            }

            for (var week = 1; week <= lastRead; week++)
            {
                var isFinal = WeekFinality.IsFinal(week, weeks.CurrentWeek);
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
            // it never reaches the radar, so there is nothing to report. Anyone else is reported
            // once per instance: a live week is rebuilt every 5 s, and an eliminated team's
            // playoff lineup may never be set either
            if (slotAverages is null && !isExcluded && reported.IsNew($"slots:{mapped.RosterId}:{week}"))
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
                slotAverages ?? new Dictionary<string, decimal>(),
                mapped.MatchupId ?? 0);
        }

        /// <summary>
        /// Groups the weeks into one season per roster, keeping only the rosters asked for.
        /// </summary>
        /// <param name="weeks">Every roster's weeks.</param>
        /// <param name="teams">The league's teams, for their names.</param>
        /// <param name="include">Which rosters to keep: the counted ones, or the excluded ones (A1).</param>
        /// <returns>One season per kept roster, in roster order.</returns>
        /// <remarks>
        /// A1 is applied here: the snapshot keeps excluded rosters in a list of
        /// their own, so nothing downstream that counts can reach them (T68). Both
        /// invariants have already checked them.
        /// </remarks>
        private static IReadOnlyList<TeamSeason> GroupTeams(
            IEnumerable<TeamWeek> weeks,
            IReadOnlyDictionary<int, SleeperTeam> teams,
            Func<int, bool> include) =>
            [.. weeks
                .Where(w => include(w.RosterId))
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
        /// Logs each invariant breach, record mismatch and unresolved player the
        /// first time this instance sees it, rather than on every build.
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

            foreach (var mismatch in snapshot.RecordMismatches.Where(m => reported.IsNew($"record:{m.RosterId}:{m.OurRecord.Games}")))
            {
                // T68: the third check; logged in either format, so the record is proven before it is shown
                logger.LogError(
                    "Roster {RosterId}'s head-to-head record after {Games} results is {Ours}, but Sleeper's is {Sleepers}.",
                    mismatch.RosterId, mismatch.OurRecord.Games, WinLoss(mismatch.OurRecord), WinLoss(mismatch.SleeperRecord));
            }

            foreach (var playerId in snapshot.UnresolvedPlayerIds.Where(id => reported.IsNew($"player:{id}")))
            {
                logger.LogWarning("Rostered player {PlayerId} is missing from the player map.", playerId);
            }
        }

        /// <inheritdoc />
        public async Task<int> BackfillMatchupsAsync(CancellationToken cancellationToken)
        {
            var read = 0;

            // newest first, so a season Sleeper no longer serves cannot starve the current one
            foreach (var (season, leagueId) in (await chain.GetAsync(cancellationToken)).OrderByDescending(s => s.Key))
            {
                if (read >= BackfillWeeksPerRun)
                {
                    break;
                }

                try
                {
                    read += await BackfillSeasonAsync(season, leagueId, BackfillWeeksPerRun - read, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Could not backfill matchups for season {Season}; the other seasons carry on.", season);
                }
            }

            return read;
        }

        /// <summary>
        /// Backfills one season, reading no more weeks than the run has left.
        /// </summary>
        /// <param name="season">The season whose stored weeks are filled in.</param>
        /// <param name="leagueId">That season's league.</param>
        /// <param name="budget">Weeks the run may still read, at least one.</param>
        /// <returns>How many weeks were read.</returns>
        /// <remarks>
        /// Only stored weeks are touched, and only their game is set: ideal points are
        /// never recomputed against today's player map (D2). The excluded rosters' weeks
        /// that were never stored are built from the read; their ideal points use
        /// today's map, which is harmless because they count in nothing but the pairing.
        /// The whole season is written back, as the store keeps a season, not a subset.
        /// </remarks>
        private async Task<int> BackfillSeasonAsync(int season, string leagueId, int budget, CancellationToken cancellationToken)
        {
            // decided from the store alone, so a complete season costs no Sleeper call. A week stored
            // before T68 has no game on any row, and its placeholder rows are added in the same pass
            var weeks = (await stored.ReadAsync(leagueId, season, cancellationToken))
                .GroupBy(w => w.Week)
                .ToDictionary(g => g.Key, g => g.ToList());
            var missing = weeks.Where(w => w.Value.Any(row => row.MatchupId is null)).Select(w => w.Key).Order().ToList();
            if (missing.Count == 0)
            {
                if (weeks.Count > 0 && reported.IsNew($"matchups-complete:{season}"))
                {
                    logger.LogInformation("Matchups complete for season {Season}.", season);
                }

                return 0;
            }

            var toRead = missing.Take(budget).ToList();
            var league = await sleeper.GetLeagueAsync(leagueId, cancellationToken);
            var excluded = ResolveExclusions(league, options.Value, season);
            var playerMap = await players.EnsureCurrentAsync(cancellationToken);
            var (filled, added) = (0, 0);
            foreach (var week in toRead)
            {
                var read = (await sleeper.GetWeekAsync(leagueId, week, playerMap, cancellationToken)).ToDictionary(r => r.RosterId);
                var rows = weeks[week];
                for (var i = 0; i < rows.Count; i++)
                {
                    if (rows[i].MatchupId is null && read.TryGetValue(rows[i].RosterId, out var roster))
                    {
                        rows[i] = rows[i] with { MatchupId = roster.MatchupId ?? 0 };
                        filled++;
                    }
                }

                foreach (var id in excluded.Where(id => read.ContainsKey(id) && rows.All(row => row.RosterId != id)))
                {
                    rows.Add(ToTeamWeek(read[id], week, league.Slots, isFinal: true, isExcluded: true));
                    added++;
                }

                if (rows.Any(row => row.MatchupId is null) && reported.IsNew($"matchups-unread:{season}:{week}"))
                {
                    // an old league Sleeper no longer serves: read again each run, inside the budget,
                    // and that season's records stay unknown
                    logger.LogWarning("Sleeper returned no game for some rosters in week {Week} of {Season}; left unfilled.", week, season);
                }
            }

            if (filled + added > 0)
            {
                await stored.WriteAsync(leagueId, season, [.. weeks.Values.SelectMany(w => w)], cancellationToken);
            }

            logger.LogInformation(
                "Backfilled season {Season}: {Read} weeks read, {Filled} games filled, {Added} excluded weeks added, {Left} weeks left.",
                season, toRead.Count, filled, added, missing.Count - toRead.Count);

            return toRead.Count;
        }

        /// <summary>
        /// Writes a record as wins, losses and ties.
        /// </summary>
        /// <param name="record">A roster's results.</param>
        /// <returns>For example, <c>13-5-0</c>.</returns>
        private static string WinLoss(TeamRecord record) => $"{record.Wins}-{record.Losses}-{record.Ties}";

        /// <summary>
        /// A past season's build, null when it cannot be served, wrapped so the
        /// cache holds the answer either way and hands back one instance.
        /// </summary>
        /// <param name="Snapshot">The season, or null.</param>
        [ImmutableObject(true)]
        private sealed record PastBuild(SeasonSnapshot? Snapshot);
    }
}
