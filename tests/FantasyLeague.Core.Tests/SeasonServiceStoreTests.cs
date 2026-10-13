using FantasyLeague.Seasons;
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
        private static async Task<ISeasonService> BuildAsync(
            LeagueSocket socket, IFinalizedWeekStore store, params (string Key, string? Value)[] settings)
        {
            var services = TestServices.Create(settings);
            services.AddSingleton(store);
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                    .ConfigurePrimaryHttpMessageHandler(() => socket);
            services.AddSeasonServiceWithNullStores();

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

            await (await BuildAsync(ThreeWeeks(), store)).BuildAsync(TestContext.Current.CancellationToken);

            store.Reads.Should().Be(1, "one read per season, not one per week");
        }

        [Fact]
        public async Task A_stored_week_is_served_instead_of_rebuilt()
        {
            var stored = new TeamWeek(1, 1, 999m, 999m, [], [], [], true, new Dictionary<string, decimal>());
            var store = new RecordingWeekStore([stored]);

            var snapshot = await (await BuildAsync(ThreeWeeks(), store)).BuildAsync(TestContext.Current.CancellationToken);

            var week1 = snapshot.Teams.Single(t => t.RosterId == 1).Weeks.Single(w => w.Week == 1);
            week1.ActualPts.Should().Be(999m, "the stored value wins over Sleeper's");
        }

        [Fact]
        public async Task A_week_still_in_progress_is_never_served_from_the_store()
        {
            // week 3 is current, so it must come from Sleeper however the store answers
            var stored = new TeamWeek(1, 3, 999m, 999m, [], [], [], true, new Dictionary<string, decimal>());
            var store = new RecordingWeekStore([stored]);

            var snapshot = await (await BuildAsync(ThreeWeeks(), store)).BuildAsync(TestContext.Current.CancellationToken);

            var week3 = snapshot.Teams.Single(t => t.RosterId == 1).Weeks.Single(w => w.Week == 3);
            week3.ActualPts.Should().NotBe(999m);
        }

        [Fact]
        public async Task An_empty_store_rebuilds_every_week_from_Sleeper()
        {
            var snapshot = await (await BuildAsync(ThreeWeeks(), new RecordingWeekStore()))
                .BuildAsync(TestContext.Current.CancellationToken);

            snapshot.Teams.Single(t => t.RosterId == 1).Weeks.Should().HaveCount(3);
        }

        [Fact]
        public async Task A_rebuilt_week_carries_its_rostered_players()
        {
            // what the widening was for: stored weeks must be re-solvable later
            var snapshot = await (await BuildAsync(ThreeWeeks(), new RecordingWeekStore()))
                .BuildAsync(TestContext.Current.CancellationToken);

            var week = snapshot.Teams.Single(t => t.RosterId == 1).Weeks.First();
            week.Players.Should().NotBeEmpty();
            week.Players.Should().OnlyContain(p => p.FantasyPositions.Count > 0);
        }

        [Fact]
        public async Task An_excluded_roster_never_reaches_the_snapshot()
        {
            // A1, at the read boundary: a stored week of the placeholder team is
            // dropped as well as a fetched one
            var socket = new LeagueSocket { State = Bodies.State(week: 2) };
            socket.Matchups[2] = Bodies.Week(Bodies.Matchup(1, 35m), Bodies.Matchup(2, 35m));
            var store = new RecordingWeekStore([
                new TeamWeek(1, 1, 35m, 35m, [], [], [], true, new Dictionary<string, decimal>()),
                new TeamWeek(2, 1, 35m, 35m, [], [], [], true, new Dictionary<string, decimal>()),
            ]);

            var service = await BuildAsync(socket, store, ("League:ExcludedOwnerIds:0", "u2"));
            var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

            snapshot.Teams.Should().ContainSingle().Which.RosterId.Should().Be(1);
            snapshot.Teams[0].Weeks.Select(w => w.Week).Should().Equal(1, 2);
        }

        [Fact]
        public async Task The_league_reference_leaves_out_the_excluded_roster()
        {
            // A1 step 2: the radar's league average is a separate aggregate from
            // the standings, and the placeholder's low scores must not reach it
            var socket = new LeagueSocket { State = Bodies.State(week: 2) };
            socket.Matchups[1] = Bodies.Week(
                Bodies.Matchup(1, 35m),
                Bodies.Matchup(2, 3m, startersPoints: [1m, 1m, 1m],
                    playersPoints: new Dictionary<string, decimal> { ["qb1"] = 1m, ["rb1"] = 1m, ["wr1"] = 1m }));

            var service = await BuildAsync(socket, new RecordingWeekStore(), ("League:ExcludedOwnerIds:0", "u2"));
            var payload = SeasonPayloadMapper.ToSeasonPayload(
                await service.BuildAsync(TestContext.Current.CancellationToken), DateTimeOffset.UnixEpoch);

            payload.Teams.Should().ContainSingle();
            payload.LeagueSlotAverages.Should().NotBeEmpty()
                .And.BeEquivalentTo(payload.Teams[0].SlotAverages);
        }

        [Fact]
        public async Task An_excluded_roster_is_still_checked_against_both_invariants()
        {
            // starters sum to 35 against 99 points: a breach on the placeholder is still a breach
            var socket = new LeagueSocket { State = Bodies.State(week: 1) };
            socket.Matchups[1] = Bodies.Week(Bodies.Matchup(1, 35m), Bodies.Matchup(2, 99m));

            var service = await BuildAsync(socket, new RecordingWeekStore(), ("League:ExcludedOwnerIds:0", "u2"));
            var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

            snapshot.Violations.Should().NotBeEmpty().And.OnlyContain(v => v.RosterId == 2);
        }
    }
}
