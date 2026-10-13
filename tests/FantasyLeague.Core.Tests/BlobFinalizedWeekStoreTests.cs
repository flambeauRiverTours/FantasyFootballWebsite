using FantasyLeague.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// A clock the test moves by hand.
    /// </summary>
    internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// A20: the blob holds one season, the newest it has been given.
    /// </summary>
    public class BlobFinalizedWeekStoreTests
    {
        private const string League = "L1";

        private static TeamWeek Week(decimal ideal) =>
            new(1, 1, 100m, ideal, [], [], [], true, new Dictionary<string, decimal>());

        private static BlobFinalizedWeekStore Store(MemoryContainer container, TimeProvider? time = null) =>
            new(container, time ?? TimeProvider.System, NullLogger<BlobFinalizedWeekStore>.Instance);

        [Fact]
        public async Task A_written_season_reads_back()
        {
            var store = Store(new MemoryContainer());
            await store.WriteAsync(League, 2026, [Week(110m)], TestContext.Current.CancellationToken);

            var read = await store.ReadAsync(League, 2026, TestContext.Current.CancellationToken);

            read.Should().ContainSingle().Which.IdealPts.Should().Be(110m);
        }

        [Fact]
        public async Task A_read_for_another_season_misses()
        {
            // a past season is SQL's, even if the blob happens to be warm
            var store = Store(new MemoryContainer());
            await store.WriteAsync(League, 2026, [Week(110m)], TestContext.Current.CancellationToken);

            (await store.ReadAsync(League, 2025, TestContext.Current.CancellationToken)).Should().BeNull();
        }

        [Fact]
        public async Task An_older_season_never_evicts_a_newer_one()
        {
            // a backfill of 2025 must leave the live 2026 in place
            var store = Store(new MemoryContainer());
            await store.WriteAsync(League, 2026, [Week(110m)], TestContext.Current.CancellationToken);

            await store.WriteAsync(League, 2025, [Week(90m)], TestContext.Current.CancellationToken);

            (await store.ReadAsync(League, 2026, TestContext.Current.CancellationToken)).Should().ContainSingle()
                .Which.IdealPts.Should().Be(110m);
        }

        [Fact]
        public async Task A_newer_season_replaces_the_old_one()
        {
            var store = Store(new MemoryContainer());
            await store.WriteAsync(League, 2026, [Week(110m)], TestContext.Current.CancellationToken);

            await store.WriteAsync(League, 2027, [Week(130m)], TestContext.Current.CancellationToken);

            (await store.ReadAsync(League, 2027, TestContext.Current.CancellationToken)).Should().ContainSingle();
            (await store.ReadAsync(League, 2026, TestContext.Current.CancellationToken)).Should().BeNull();
        }

        [Fact]
        public async Task A_season_written_before_the_last_finalize_run_misses()
        {
            // Tuesday's job reached SQL but not the blob: the blob must not hide the new week
            var container = new MemoryContainer();
            var clock = new ManualClock(new DateTimeOffset(2026, 10, 12, 18, 0, 0, TimeSpan.Zero)); // Monday
            await Store(container, clock).WriteAsync(League, 2026, [Week(110m)], TestContext.Current.CancellationToken);

            clock.Now = new DateTimeOffset(2026, 10, 13, 12, 30, 0, TimeSpan.Zero); // Tuesday, after the run

            (await Store(container, clock).ReadAsync(League, 2026, TestContext.Current.CancellationToken)).Should().BeNull();
        }

        [Fact]
        public async Task A_season_written_after_the_last_finalize_run_reads_back()
        {
            var container = new MemoryContainer();
            var clock = new ManualClock(new DateTimeOffset(2026, 10, 13, 12, 5, 0, TimeSpan.Zero)); // the job's own write
            await Store(container, clock).WriteAsync(League, 2026, [Week(110m)], TestContext.Current.CancellationToken);

            clock.Now = new DateTimeOffset(2026, 10, 19, 20, 0, 0, TimeSpan.Zero); // the next Monday

            (await Store(container, clock).ReadAsync(League, 2026, TestContext.Current.CancellationToken)).Should().ContainSingle();
        }

        [Fact]
        public async Task A_season_stored_in_the_offseason_stays_current_until_September()
        {
            // no job runs from March to August, so nothing newer can exist
            var container = new MemoryContainer();
            var clock = new ManualClock(new DateTimeOffset(2027, 3, 2, 9, 0, 0, TimeSpan.Zero));
            await Store(container, clock).WriteAsync(League, 2026, [Week(110m)], TestContext.Current.CancellationToken);

            clock.Now = new DateTimeOffset(2027, 8, 31, 9, 0, 0, TimeSpan.Zero);

            (await Store(container, clock).ReadAsync(League, 2026, TestContext.Current.CancellationToken)).Should().ContainSingle();
        }

        [Fact]
        public async Task The_blob_name_never_carries_the_league_id()
        {
            // A2: blob names show in the portal and in storage logs
            const string leagueId = "123456789012345678";
            var container = new MemoryContainer();

            await Store(container).WriteAsync(leagueId, 2026, [Week(110m)], TestContext.Current.CancellationToken);

            container.Blobs.Keys.Should().ContainSingle().Which.Should().NotContain(leagueId);
        }

        [Fact]
        public async Task Two_leagues_keep_separate_blobs()
        {
            var container = new MemoryContainer();
            await Store(container).WriteAsync("L1", 2026, [Week(110m)], TestContext.Current.CancellationToken);
            await Store(container).WriteAsync("L2", 2026, [Week(90m)], TestContext.Current.CancellationToken);

            (await Store(container).ReadAsync("L1", 2026, TestContext.Current.CancellationToken)).Should().ContainSingle()
                .Which.IdealPts.Should().Be(110m);
        }

        [Fact]
        public async Task An_empty_season_is_not_stored()
        {
            var container = new MemoryContainer();

            await Store(container).WriteAsync(League, 2026, [], TestContext.Current.CancellationToken);

            container.Blobs.Should().BeEmpty();
        }
    }
}
