using FantasyLeague.Seasons;
using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Weeks stored per league and season, as the real store keys them.
    /// </summary>
    internal sealed class SeasonStore : IFinalizedWeekStore
    {
        private readonly Dictionary<(string League, int Season), IReadOnlyList<TeamWeek>> weeks = [];

        public SeasonStore With(string leagueId, int season, params TeamWeek[] stored)
        {
            weeks[(leagueId, season)] = stored;
            return this;
        }

        public Task<IReadOnlyList<TeamWeek>> ReadAsync(string leagueId, int season, CancellationToken cancellationToken) =>
            Task.FromResult(weeks.GetValueOrDefault((leagueId, season), []));

        public List<(string League, int Season)> Writes { get; } = [];

        public IReadOnlyList<TeamWeek> Stored(string leagueId, int season) => weeks.GetValueOrDefault((leagueId, season), []);

        public Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> stored, CancellationToken cancellationToken)
        {
            // a season at a time, as the real store keeps it
            weeks[(leagueId, season)] = stored;
            Writes.Add((leagueId, season));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Past seasons (T29, A34). 2026 is the league's first real season, so the
    /// chain here is invented: L1 plays 2026 and rolled over from L0, 2025.
    /// </summary>
    public class SeasonHistoryTests
    {
        private static LeagueSocket TwoSeasons()
        {
            var socket = new LeagueSocket { State = Bodies.State(week: 3) };
            socket.Leagues["L1"] = Bodies.League(2026, "L0");
            socket.Leagues["L0"] = Bodies.League(2025, null, "Last Year");
            return socket;
        }

        private static TeamWeek Week(int rosterId, int week, decimal actual) =>
            new(rosterId, week, actual, actual + 10m, [], [], [], true, new Dictionary<string, decimal>(), null);

        private static Task<(ISeasonService Service, ISeasonChain Chain)> BuildAsync(
            LeagueSocket socket, IFinalizedWeekStore store, params (string Key, string? Value)[] settings) =>
            BuildAsync(socket, store, null, settings);

        /// <param name="options">Supplied directly, past the binding and its validation, when not null.</param>
        private static async Task<(ISeasonService Service, ISeasonChain Chain)> BuildAsync(
            LeagueSocket socket, IFinalizedWeekStore store, LeagueOptions? options, params (string Key, string? Value)[] settings)
        {
            var services = TestServices.Create(settings);
            services.AddSingleton(store);
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                    .ConfigurePrimaryHttpMessageHandler(() => socket);
            services.AddSeasonServiceWithNullStores();
            if (options is not null)
            {
                services.AddSingleton(Options.Create(options));
            }

            var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IPlayerMapRefresher>().RefreshAsync(default);

            return (provider.GetRequiredService<ISeasonService>(), provider.GetRequiredService<ISeasonChain>());
        }

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task The_chain_walks_back_from_the_configured_league()
        {
            var (_, chain) = await BuildAsync(TwoSeasons(), new SeasonStore());

            (await chain.GetAsync(Token)).Should().BeEquivalentTo(new Dictionary<int, string> { [2026] = "L1", [2025] = "L0" });
        }

        [Fact]
        public async Task A_first_season_is_a_chain_of_one()
        {
            var (_, chain) = await BuildAsync(new LeagueSocket(), new SeasonStore());

            (await chain.GetAsync(Token)).Keys.Should().Equal(2026);
        }

        [Fact]
        public async Task A_chain_that_loops_back_on_itself_stops()
        {
            var socket = TwoSeasons();
            socket.Leagues["L0"] = Bodies.League(2025, "L1");

            var (_, chain) = await BuildAsync(socket, new SeasonStore());

            (await chain.GetAsync(Token)).Keys.Should().BeEquivalentTo([2026, 2025]);
        }

        [Fact]
        public async Task A_past_season_is_built_from_its_stored_weeks_and_its_own_league()
        {
            var socket = TwoSeasons();
            var store = new SeasonStore().With("L0", 2025, Week(1, 1, 90m), Week(1, 2, 110m), Week(2, 1, 80m), Week(2, 2, 100m));
            var (service, _) = await BuildAsync(socket, store);

            var snapshot = await service.BuildPastAsync(2025, Token);

            snapshot.Should().NotBeNull();
            snapshot!.IsCurrent.Should().BeFalse();
            snapshot.Season.Should().Be(2025);
            snapshot.LeagueName.Should().Be("Last Year");
            snapshot.FinalThroughWeek.Should().Be(2, "every stored week is final");
            snapshot.Teams.Select(t => t.Weeks.Sum(w => w.ActualPts)).Should().Equal(200m, 180m);
            socket.MatchupRequests.Should().Be(0, "a completed season never asks Sleeper for matchups");
        }

        [Fact]
        public async Task A_past_season_counts_its_regular_season_only()
        {
            // T64 applies to past seasons too, from that year's own league settings
            var socket = TwoSeasons();
            socket.Leagues["L0"] = Bodies.League(2025, null, "Last Year", playoffWeekStart: 2);
            var store = new SeasonStore().With("L0", 2025, Week(1, 1, 90m), Week(1, 2, 300m));
            var (service, _) = await BuildAsync(socket, store);

            var snapshot = (await service.BuildPastAsync(2025, Token))!;
            var payload = SeasonPayloadMapper.ToSeasonPayload(snapshot, DateTimeOffset.UnixEpoch);

            snapshot.RegularSeasonCutoff.Should().Be(1);
            payload.Teams[0].Totals.Actual.Should().Be(90m);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        public async Task A_week_that_goes_back_past_stored_weeks_cannot_shrink_the_season(int resetWeek)
        {
            // A37: if Sleeper's state resets after the NFL season, the stored season stands
            var socket = TwoSeasons();
            socket.State = Bodies.State(week: resetWeek);
            var store = new SeasonStore().With("L1", 2026, Week(1, 1, 90m), Week(1, 2, 80m), Week(1, 3, 70m));
            var (service, _) = await BuildAsync(socket, store);

            var snapshot = await service.BuildAsync(Token);

            snapshot.Teams[0].Weeks.Select(w => w.Week).Should().Equal(1, 2, 3);
            snapshot.Teams[0].Weeks.Should().OnlyContain(w => w.IsFinal);

            // the NFL season is over, so the weeks the league never played are trimmed and it is complete
            var payload = SeasonPayloadMapper.ToSeasonPayload(snapshot, DateTimeOffset.UnixEpoch);
            payload.LastRegularWeek.Should().Be(3);
            payload.FinalThroughWeek.Should().Be(3);
        }

        [Fact]
        public async Task After_a_reset_week_18_is_read_even_when_the_store_stops_short_of_it()
        {
            // A46: Sleeper may reset straight from week 18 to week 1, so week 18 never became final while live
            var socket = TwoSeasons();
            socket.State = Bodies.State(week: 1);
            var store = new SeasonStore().With("L1", 2026, [.. Enumerable.Range(1, 17).Select(w => Week(1, w, 10m))]);
            var (service, _) = await BuildAsync(socket, store);

            await service.BuildAsync(Token);

            socket.Requests.Should().Contain(p => p.EndsWith("/matchups/18", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_week_that_goes_back_mid_season_reads_no_week_past_the_store()
        {
            // a blip in Sleeper's state must not read, mark final and so store weeks nobody has played
            var socket = TwoSeasons();
            socket.State = Bodies.State(week: 2);
            var store = new SeasonStore().With("L1", 2026, [.. Enumerable.Range(1, 9).Select(w => Week(1, w, 10m))]);
            var (service, _) = await BuildAsync(socket, store);

            var snapshot = await service.BuildAsync(Token);

            socket.Requests.Should().NotContain(p => p.Contains("/matchups/", StringComparison.Ordinal));
            snapshot.Teams[0].Weeks.Select(w => w.Week).Should().Equal(Enumerable.Range(1, 9));
        }

        [Fact]
        public async Task A_failed_bracket_read_is_held_across_every_instance_of_the_service()
        {
            // the service is created per request; the hold must outlive it (A46)
            var socket = TwoSeasons();
            socket.Leagues["L0"] = Bodies.League(2025, null, "Last Year", playoffWeekStart: 2);
            socket.Bracket = null;
            var store = new SeasonStore().With("L0", 2025, Week(1, 1, 90m), Week(1, 2, 80m), Week(2, 1, 70m), Week(2, 2, 60m));
            var services = TestServices.Create();
            services.AddSingleton<IFinalizedWeekStore>(store);
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero).ConfigurePrimaryHttpMessageHandler(() => socket);
            services.AddSeasonServiceWithNullStores();
            services.AddSingleton(Options.Create(new LeagueOptions { CurrentLeagueId = "L1", Format = LeagueFormat.HeadToHead }));
            using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IPlayerMapRefresher>().RefreshAsync(Token);

            await provider.GetRequiredService<ISeasonService>().BuildPastAsync(2025, Token);
            var afterFirst = socket.BracketRequests;
            await provider.GetRequiredService<ISeasonService>().BuildPastAsync(2025, Token);

            afterFirst.Should().BeGreaterThan(0);
            socket.BracketRequests.Should().Be(afterFirst);
        }

        [Fact]
        public async Task The_bracket_is_read_only_in_head_to_head_with_playoffs_from_their_first_week()
        {
            var socket = TwoSeasons();
            socket.Leagues["L1"] = Bodies.League(2026, "L0", playoffWeekStart: 3);
            var h2h = new LeagueOptions { CurrentLeagueId = "L1", Format = LeagueFormat.HeadToHead };

            socket.State = Bodies.State(week: 2);
            var (before, _) = await BuildAsync(socket, new SeasonStore(), h2h);
            await before.BuildAsync(Token);
            socket.BracketRequests.Should().Be(0, "week 2 is before the playoffs");

            socket.State = Bodies.State(week: 3);
            var (during, _) = await BuildAsync(socket, new SeasonStore(), h2h);
            await during.BuildAsync(Token);
            socket.BracketRequests.Should().Be(1);

            var (points, _) = await BuildAsync(socket, new SeasonStore());
            await points.BuildAsync(Token);
            socket.BracketRequests.Should().Be(1, "a total-points league never reads the bracket");
        }

        [Fact]
        public async Task A_failed_bracket_read_is_held_and_never_fails_the_season()
        {
            var socket = TwoSeasons();
            socket.Leagues["L0"] = Bodies.League(2025, null, "Last Year", playoffWeekStart: 2);
            socket.Bracket = null;
            var store = new SeasonStore().With("L0", 2025, Week(1, 1, 90m), Week(1, 2, 80m), Week(2, 1, 70m), Week(2, 2, 60m));
            var (service, _) = await BuildAsync(socket, store, new LeagueOptions { CurrentLeagueId = "L1", Format = LeagueFormat.HeadToHead });

            var first = await service.BuildPastAsync(2025, Token);
            var afterFirst = socket.BracketRequests;
            var second = await service.BuildPastAsync(2025, Token);

            first!.Bracket.Should().BeNull("the season is served without it");
            second!.Bracket.Should().BeNull();
            afterFirst.Should().BeGreaterThan(0);
            socket.BracketRequests.Should().Be(afterFirst, "a failure is held, not retried at once");
        }

        [Fact]
        public async Task A_past_season_reads_its_bracket_outside_its_day_long_cache()
        {
            var socket = TwoSeasons();
            socket.Leagues["L0"] = Bodies.League(2025, null, "Last Year", playoffWeekStart: 2);
            socket.Bracket = """[{"r":1,"m":1,"t1":1,"t2":2,"w":1,"l":2,"p":1}]""";
            var store = new SeasonStore().With("L0", 2025, Week(1, 1, 90m), Week(1, 2, 80m), Week(2, 1, 70m), Week(2, 2, 60m));
            var (service, _) = await BuildAsync(socket, store, new LeagueOptions { CurrentLeagueId = "L1", Format = LeagueFormat.HeadToHead });

            var snapshot = (await service.BuildPastAsync(2025, Token))!;

            snapshot.Bracket.Should().ContainSingle().Which.Winner.Should().Be(1);
        }

        [Fact]
        public async Task A_past_season_stored_short_of_week_18_ends_at_its_last_played_week()
        {
            var store = new SeasonStore().With("L0", 2025, Week(1, 1, 90m), Week(1, 2, 80m));
            var (service, _) = await BuildAsync(TwoSeasons(), store);

            var payload = SeasonPayloadMapper.ToSeasonPayload((await service.BuildPastAsync(2025, Token))!, DateTimeOffset.UnixEpoch);

            payload.LastRegularWeek.Should().Be(2);
            payload.FinalThroughWeek.Should().Be(2);
        }

        [Fact]
        public async Task Every_season_is_shown_in_the_configured_format()
        {
            // Q10: a past season too, whatever format the league played it in. The setting
            // refuses HeadToHead until T72 (A38), so the options are supplied directly.
            var (service, _) = await BuildAsync(
                TwoSeasons(),
                new SeasonStore().With("L0", 2025, Week(1, 1, 90m)),
                new LeagueOptions { CurrentLeagueId = "L1", Format = LeagueFormat.HeadToHead });

            (await service.BuildAsync(Token)).Format.Should().Be(LeagueFormat.HeadToHead);
            (await service.BuildPastAsync(2025, Token))!.Format.Should().Be(LeagueFormat.HeadToHead);
        }

        [Fact]
        public async Task A_past_season_leaves_out_the_placeholder_team()
        {
            var store = new SeasonStore().With("L0", 2025, Week(1, 1, 90m), Week(2, 1, 0m));
            var (service, _) = await BuildAsync(TwoSeasons(), store, ("League:ExcludedOwnerIds:0", "u2"));

            var snapshot = (await service.BuildPastAsync(2025, Token))!;

            snapshot.Teams.Should().ContainSingle().Which.RosterId.Should().Be(1);
            snapshot.ExcludedTeams.Should().ContainSingle().Which.RosterId.Should().Be(2, "kept apart for head-to-head (T68)");
        }

        private static TeamWeek Week(int rosterId, int week, decimal actual, int? matchupId) =>
            Week(rosterId, week, actual) with { MatchupId = matchupId };

        [Fact]
        public async Task The_backfill_fills_each_game_and_stores_the_placeholder_s_weeks()
        {
            // T68: a week stored before matchups were read, and without the placeholder's side of it
            var store = new SeasonStore().With("L1", 2026, Week(1, 1, 35m) with { IdealPts = 999m });
            var socket = TwoSeasons();
            socket.Matchups[1] = Bodies.Week(Bodies.Matchup(1, 35m, matchupId: 3), Bodies.Matchup(2, 30m, matchupId: 3));
            var (service, _) = await BuildAsync(socket, store, ("League:ExcludedOwnerIds:0", "u2"));

            (await service.BackfillMatchupsAsync(Token)).Should().Be(1);

            var weeks = store.Stored("L1", 2026).ToDictionary(w => w.RosterId);
            weeks[1].MatchupId.Should().Be(3);
            weeks[1].IdealPts.Should().Be(999m, "a stored week is never re-solved against today's player map (D2)");
            weeks[2].MatchupId.Should().Be(3, "the placeholder's week is stored, so the game pairs");
        }

        [Fact]
        public async Task A_complete_backfill_reads_nothing()
        {
            var store = new SeasonStore().With("L1", 2026, Week(1, 1, 35m, matchupId: 1), Week(2, 1, 30m, matchupId: 1));
            var socket = TwoSeasons();
            var (service, _) = await BuildAsync(socket, store);

            (await service.BackfillMatchupsAsync(Token)).Should().Be(0);
            socket.Requests.Should().NotContain(
                path => path.Contains("/matchups/", StringComparison.Ordinal) || path.EndsWith("/rosters", StringComparison.Ordinal),
                "a complete season is decided from the store alone; only the cached chain walk reaches Sleeper");
            store.Writes.Should().BeEmpty();
        }

        [Fact]
        public async Task The_backfill_reads_at_most_twelve_weeks_a_run_and_finishes_on_the_next()
        {
            // the live site shares the token bucket
            var store = new SeasonStore().With("L1", 2026, [.. Enumerable.Range(1, 15).SelectMany(w => new[] { Week(1, w, 35m), Week(2, w, 30m) })]);
            var socket = TwoSeasons();
            socket.DefaultMatchups = Bodies.Week(Bodies.Matchup(1, 35m, matchupId: 1), Bodies.Matchup(2, 30m, matchupId: 1));
            var (service, _) = await BuildAsync(socket, store);

            (await service.BackfillMatchupsAsync(Token)).Should().Be(12);
            (await service.BackfillMatchupsAsync(Token)).Should().Be(3);
            (await service.BackfillMatchupsAsync(Token)).Should().Be(0);
            store.Stored("L1", 2026).Should().OnlyContain(w => w.MatchupId == 1);
        }

        [Fact]
        public async Task A_week_Sleeper_no_longer_serves_is_left_unfilled()
        {
            var store = new SeasonStore().With("L1", 2026, Week(1, 1, 35m));
            var (service, _) = await BuildAsync(TwoSeasons(), store);

            (await service.BackfillMatchupsAsync(Token)).Should().Be(1);
            store.Stored("L1", 2026).Should().ContainSingle().Which.MatchupId.Should().BeNull();
        }

        [Fact]
        public async Task A_season_that_fails_does_not_stop_the_others()
        {
            var store = new FailingSeason(new SeasonStore().With("L0", 2025, Week(1, 1, 35m), Week(2, 1, 30m)), failing: 2026);
            var socket = TwoSeasons();
            socket.Matchups[1] = Bodies.Week(Bodies.Matchup(1, 35m, matchupId: 2), Bodies.Matchup(2, 30m, matchupId: 2));
            var (service, _) = await BuildAsync(socket, store);

            (await service.BackfillMatchupsAsync(Token)).Should().Be(1);
            store.Inner.Stored("L0", 2025).Should().OnlyContain(w => w.MatchupId == 2);
        }

        /// <summary>A store that cannot read one season.</summary>
        private sealed class FailingSeason(SeasonStore inner, int failing) : IFinalizedWeekStore
        {
            public SeasonStore Inner => inner;

            public Task<IReadOnlyList<TeamWeek>> ReadAsync(string leagueId, int season, CancellationToken cancellationToken) =>
                season == failing ? throw new InvalidOperationException("unreadable") : inner.ReadAsync(leagueId, season, cancellationToken);

            public Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken cancellationToken) =>
                inner.WriteAsync(leagueId, season, weeks, cancellationToken);
        }

        [Fact]
        public async Task A_season_the_chain_does_not_reach_is_absent()
        {
            var (service, _) = await BuildAsync(TwoSeasons(), new SeasonStore());

            (await service.BuildPastAsync(2019, Token)).Should().BeNull();
        }

        [Fact]
        public async Task A_reachable_season_with_nothing_stored_is_absent()
        {
            var (service, _) = await BuildAsync(TwoSeasons(), new SeasonStore());

            (await service.BuildPastAsync(2025, Token)).Should().BeNull("never synchronously backfilled (design §09)");
        }

        [Fact]
        public async Task Only_seasons_with_stored_weeks_are_available_besides_the_current_one()
        {
            var (withHistory, _) = await BuildAsync(TwoSeasons(), new SeasonStore().With("L0", 2025, Week(1, 1, 90m)));
            var (withoutHistory, _) = await BuildAsync(TwoSeasons(), new SeasonStore());

            (await withHistory.GetAvailableSeasonsAsync(Token)).Should().Equal(2026, 2025);
            (await withoutHistory.GetAvailableSeasonsAsync(Token)).Should().Equal(2026);
        }

        [Fact]
        public async Task The_current_season_is_still_live()
        {
            var socket = TwoSeasons();
            socket.Matchups[1] = Bodies.Week(Bodies.Matchup(1, 35m));
            var (service, _) = await BuildAsync(socket, new SeasonStore());

            var snapshot = await service.BuildAsync(Token);

            snapshot.IsCurrent.Should().BeTrue();
            SeasonPayloadMapper.ToSeasonPayload(snapshot, DateTimeOffset.UnixEpoch).IsCurrent.Should().BeTrue();
        }
    }
}
