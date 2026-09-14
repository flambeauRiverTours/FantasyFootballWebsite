using FantasyLeague.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantasyLeague.Core.Tests
{
    internal sealed class InMemoryContextFactory(string name) : IDbContextFactory<FantasyDataContext>
    {
        public FantasyDataContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<FantasyDataContext>()
                .UseInMemoryDatabase(name)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
    }

    internal sealed class BrokenContextFactory : IDbContextFactory<FantasyDataContext>
    {
        public FantasyDataContext CreateDbContext() =>
            throw new InvalidOperationException("no database");
    }

    public class SqlFinalizedWeekStoreTests
    {
        private const string League = "L1";

        private static SqlFinalizedWeekStore Store() =>
            new(new InMemoryContextFactory(Guid.NewGuid().ToString()),
                NullLogger<SqlFinalizedWeekStore>.Instance);

        private static TeamWeek Week(
            int rosterId = 1,
            int week = 1,
            decimal actual = 100m,
            decimal ideal = 120m,
            bool isFinal = true,
            IReadOnlyList<RosteredPlayer>? players = null) =>
            new(rosterId, week, actual, ideal,
                [new PlayerId("s1"), new PlayerId("s2")],
                [new PlayerId("s1"), new PlayerId("i9")],
                players ?? [new RosteredPlayer(new PlayerId("s1"), "Patrick Mahomes", ["QB"], 27.5m)],
                isFinal);

        [Fact]
        public async Task An_empty_store_reads_back_nothing()
        {
            (await Store().ReadAsync(League, 2026, default)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_written_week_reads_back()
        {
            var store = Store();

            await store.WriteAsync(League, 2026, [Week()], default);
            var read = await store.ReadAsync(League, 2026, default);

            read.Should().ContainSingle();
            read[0].RosterId.Should().Be(1);
            read[0].Week.Should().Be(1);
        }

        [Fact]
        public async Task The_scores_survive_the_round_trip_exactly()
        {
            var store = Store();

            await store.WriteAsync(League, 2026, [Week(actual: 123.45m, ideal: 167.89m)], default);
            var read = await store.ReadAsync(League, 2026, default);

            read[0].Actual.Should().Be(123.45m);
            read[0].Ideal.Should().Be(167.89m);
        }

        [Fact]
        public async Task The_lineups_survive_the_round_trip()
        {
            var store = Store();

            await store.WriteAsync(League, 2026, [Week()], default);
            var read = await store.ReadAsync(League, 2026, default);

            read[0].Starters.Should().Equal(new PlayerId("s1"), new PlayerId("s2"));
            read[0].IdealLineup.Should().Equal(new PlayerId("s1"), new PlayerId("i9"));
        }
        [Fact]
        public async Task The_rostered_players_survive_the_round_trip()
        {
            // the whole reason TeamWeek was widened: a stored week must be re-solvable
            var store = Store();

            await store.WriteAsync(League, 2026, [Week(players:
                [
                    new RosteredPlayer(new PlayerId("4046"), "Patrick Mahomes", ["QB"], 27.5m),
                    new RosteredPlayer(new PlayerId("1234"), "Multi Eligible", ["RB", "WR"], 8.25m),
                ])], default);

            var read = await store.ReadAsync(League, 2026, default);

            // BeEquivalentTo, not Be: RosteredPlayer is a record whose list member
            // compares by reference, so two identical players are never ==
            read[0].Players.Should().HaveCount(2);
            read[0].Players[0].Should().BeEquivalentTo(
                new RosteredPlayer(new PlayerId("4046"), "Patrick Mahomes", ["QB"], 27.5m));
            read[0].Players[1].Points.Should().Be(8.25m);
            read[0].Players[1].FantasyPositions.Should().Equal("RB", "WR");
        }

        [Fact]
        public async Task Writing_the_same_week_twice_updates_rather_than_duplicating()
        {
            // T30 re-runs the backfill after a solver fix; that is designed, not accidental
            var store = Store();

            await store.WriteAsync(League, 2026, [Week(ideal: 120m)], default);
            await store.WriteAsync(League, 2026, [Week(ideal: 155m)], default);

            var read = await store.ReadAsync(League, 2026, default);

            read.Should().ContainSingle();
            read[0].Ideal.Should().Be(155m);
        }

        [Fact]
        public async Task Each_roster_and_week_is_its_own_row()
        {
            var store = Store();

            await store.WriteAsync(League, 2026,
                [Week(rosterId: 1, week: 1), Week(rosterId: 2, week: 1), Week(rosterId: 1, week: 2)],
                default);

            (await store.ReadAsync(League, 2026, default)).Should().HaveCount(3);
        }

        [Fact]
        public async Task A_different_season_is_not_returned()
        {
            var store = Store();

            await store.WriteAsync(League, 2025, [Week()], default);

            (await store.ReadAsync(League, 2026, default)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_different_league_is_not_returned()
        {
            // roster ids repeat across leagues; the key is what keeps them apart
            var store = Store();

            await store.WriteAsync("L1", 2026, [Week()], default);

            (await store.ReadAsync("L2", 2026, default)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_week_still_in_progress_is_never_read_back()
        {
            var store = Store();

            await store.WriteAsync(League, 2026, [Week(isFinal: false)], default);

            (await store.ReadAsync(League, 2026, default)).Should().BeEmpty();
        }

        [Fact]
        public async Task Writing_nothing_is_not_an_error()
        {
            await Store().Invoking(s => s.WriteAsync(League, 2026, [], default))
                .Should().NotThrowAsync();
        }

        [Fact]
        public async Task An_unreachable_database_reads_back_empty_rather_than_throwing()
        {
            // the caller falls back to Sleeper: correct, just slower
            var store = new SqlFinalizedWeekStore(
                new BrokenContextFactory(), NullLogger<SqlFinalizedWeekStore>.Instance);

            (await store.ReadAsync(League, 2026, default)).Should().BeEmpty();
        }

        [Fact]
        public async Task An_unreachable_database_still_throws_on_write()
        {
            // a swallowed write means a finalized week is absent forever
            var store = new SqlFinalizedWeekStore(
                new BrokenContextFactory(), NullLogger<SqlFinalizedWeekStore>.Instance);

            await store.Invoking(s => s.WriteAsync(League, 2026, [Week()], default))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }

    public class NullFinalizedWeekStoreTests
    {
        [Fact]
        public async Task It_reads_back_nothing()
        {
            (await new NullFinalizedWeekStore().ReadAsync("L1", 2026, default)).Should().BeEmpty();
        }

        [Fact]
        public async Task It_accepts_a_write_and_keeps_none_of_it()
        {
            var store = new NullFinalizedWeekStore();

            await store.WriteAsync("L1", 2026,
                [new TeamWeek(1, 1, 10m, 10m, [], [], [], true)], default);

            (await store.ReadAsync("L1", 2026, default)).Should().BeEmpty();
        }

    }
}
