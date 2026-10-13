using FantasyLeague.Seasons;
using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

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

        public Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> stored, CancellationToken cancellationToken) =>
            Task.CompletedTask;
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
            new(rosterId, week, actual, actual + 10m, [], [], [], true, new Dictionary<string, decimal>());

        private static async Task<(ISeasonService Service, ISeasonChain Chain)> BuildAsync(
            LeagueSocket socket, IFinalizedWeekStore store, params (string Key, string? Value)[] settings)
        {
            var services = TestServices.Create(settings);
            services.AddSingleton(store);
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                    .ConfigurePrimaryHttpMessageHandler(() => socket);
            services.AddSeasonServiceWithNullStores();

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
        public async Task A_past_season_leaves_out_the_placeholder_team()
        {
            var store = new SeasonStore().With("L0", 2025, Week(1, 1, 90m), Week(2, 1, 0m));
            var (service, _) = await BuildAsync(TwoSeasons(), store, ("League:ExcludedOwnerIds:0", "u2"));

            (await service.BuildPastAsync(2025, Token))!.Teams.Should().ContainSingle().Which.RosterId.Should().Be(1);
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
