using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyLeague.Core.Tests
{
    internal sealed class RecordingWeekStore(IReadOnlyList<TeamWeek>? stored = null) : IFinalizedWeekStore
    {
        public int Reads { get; private set; }

        public IReadOnlyList<TeamWeek>? Written { get; private set; }

        public Task<IReadOnlyList<TeamWeek>> ReadAsync(
            string leagueId, int season, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(stored ?? []);
        }

        public Task WriteAsync(
            string leagueId, int season,
            IReadOnlyList<TeamWeek> weeks, CancellationToken cancellationToken)
        {
            Written = weeks;
            return Task.CompletedTask;
        }
    }

    public class SeasonServiceStoreTests
    {
        private static async Task<ISeasonService> BuildAsync(LeagueSocket socket, IFinalizedWeekStore store)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(store);
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                    .ConfigurePrimaryHttpMessageHandler(() => socket);

            var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IPlayerMapRefresher>().RefreshAsync(default);

            return provider.GetRequiredService<ISeasonService>();
        }

        private static LeagueSocket ThreeWeeks()
        {
            var socket = new LeagueSocket { State = Bodies.State(week: 3) };
            socket.Matchups[1] = Bodies.Week(Bodies.Matchup(1, 35m));
            socket.Matchups[2] = Bodies.Week(Bodies.Matchup(1, 41m));
            socket.Matchups[3] = Bodies.Week(Bodies.Matchup(1, 12m));
            return socket;
        }

        [Fact]
        public async Task The_store_is_consulted_once_per_build()
        {
            var store = new RecordingWeekStore();

            await (await BuildAsync(ThreeWeeks(), store)).BuildAsync("L1", default);

            store.Reads.Should().Be(1, "one read per season, not one per week");
        }

        [Fact]
        public async Task A_stored_week_is_served_instead_of_rebuilt()
        {
            var stored = new TeamWeek(1, 1, 999m, 999m, [], [], [], true, new Dictionary<string, decimal>());
            var store = new RecordingWeekStore([stored]);

            var snapshot = await (await BuildAsync(ThreeWeeks(), store)).BuildAsync("L1", default);

            var week1 = snapshot.Teams.Single(t => t.RosterId == 1).Weeks.Single(w => w.Week == 1);
            week1.Actual.Should().Be(999m, "the stored value wins over Sleeper's");
        }

        [Fact]
        public async Task A_week_still_in_progress_is_never_served_from_the_store()
        {
            // week 3 is current, so it must come from Sleeper however the store answers
            var stored = new TeamWeek(1, 3, 999m, 999m, [], [], [], true, new Dictionary<string, decimal>());
            var store = new RecordingWeekStore([stored]);

            var snapshot = await (await BuildAsync(ThreeWeeks(), store)).BuildAsync("L1", default);

            var week3 = snapshot.Teams.Single(t => t.RosterId == 1).Weeks.Single(w => w.Week == 3);
            week3.Actual.Should().NotBe(999m);
        }

        [Fact]
        public async Task An_empty_store_rebuilds_every_week_from_Sleeper()
        {
            var snapshot = await (await BuildAsync(ThreeWeeks(), new RecordingWeekStore()))
                .BuildAsync("L1", default);

            snapshot.Teams.Single(t => t.RosterId == 1).Weeks.Should().HaveCount(3);
        }

        [Fact]
        public async Task A_rebuilt_week_carries_its_rostered_players()
        {
            // what the widening was for: stored weeks must be re-solvable later
            var snapshot = await (await BuildAsync(ThreeWeeks(), new RecordingWeekStore()))
                .BuildAsync("L1", default);

            var week = snapshot.Teams.Single(t => t.RosterId == 1).Weeks.First();
            week.Players.Should().NotBeEmpty();
            week.Players.Should().OnlyContain(p => p.FantasyPositions.Count > 0);
        }
    }
}
