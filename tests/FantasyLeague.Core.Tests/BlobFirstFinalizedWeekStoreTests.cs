using FantasyLeague.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Records what was written and hands back whatever it was primed with —
    /// the blob equivalent of <c>FakePlayerMapStore</c>.
    /// </summary>
    internal sealed class FakeBlobFinalizedWeekStore(IReadOnlyList<TeamWeek>? stored = null) : IBlobFinalizedWeekStore
    {
        public IReadOnlyList<TeamWeek>? Written { get; private set; }

        public int Reads { get; private set; }

        public Task<IReadOnlyList<TeamWeek>?> ReadAsync(string leagueId, int season, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(stored);
        }

        public Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken ct)
        {
            Written = weeks;
            return Task.CompletedTask;
        }
    }

    /// <summary>Always reports the same instant, so the season gate is testable without waiting on the calendar.</summary>
    internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public class BlobFirstFinalizedWeekStoreTests
    {
        private const string League = "L1";

        // 2026-09-20: inside the window SanityCheckSeasonForBlob treats as the
        // 2026 season (Sept 1 2026 – Mar 1 2027).
        private static readonly TimeProvider DuringTheSeason = new FixedTimeProvider(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero));

        // Mid-June: outside every season's window regardless of which year it names.
        private static readonly TimeProvider TheOffseason = new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));

        private static SqlFinalizedWeekStore SqlStore() =>
            new(new InMemoryContextFactory(Guid.NewGuid().ToString()), NullLogger<SqlFinalizedWeekStore>.Instance);

        private static TeamWeek Week(decimal ideal = 120m) =>
            new(1, 1, 100m, ideal, [], [], [], true, new Dictionary<string, decimal>());

        [Fact]
        public async Task A_read_prefers_the_blob_over_sql()
        {
            var sql = SqlStore();
            await sql.WriteAsync(League, 2026, [Week(ideal: 100m)], default);
            var blob = new FakeBlobFinalizedWeekStore([Week(ideal: 999m)]);
            var store = new BlobFirstFinalizedWeekStore(sql, blob, NullLogger<BlobFirstFinalizedWeekStore>.Instance, DuringTheSeason);

            var read = await store.ReadAsync(League, 2026, default);

            read.Should().ContainSingle();
            read[0].Ideal.Should().Be(999m);
        }

        [Fact]
        public async Task A_cold_blob_falls_through_to_sql()
        {
            // no new decision needed here (A16): this is exactly what every
            // request already does today, with nothing built
            var sql = SqlStore();
            await sql.WriteAsync(League, 2026, [Week(ideal: 100m)], default);
            var blob = new FakeBlobFinalizedWeekStore(stored: null);
            var store = new BlobFirstFinalizedWeekStore(sql, blob, NullLogger<BlobFirstFinalizedWeekStore>.Instance, DuringTheSeason);

            var read = await store.ReadAsync(League, 2026, default);

            read.Should().ContainSingle();
            read[0].Ideal.Should().Be(100m);
        }

        [Fact]
        public async Task A_cold_blob_during_the_season_is_primed_from_sql()
        {
            // T58: otherwise the blob sits empty from deploy until the first
            // Tuesday finalize run — this is what makes it self-heal instead
            var sql = SqlStore();
            await sql.WriteAsync(League, 2026, [Week(ideal: 100m)], default);
            var blob = new FakeBlobFinalizedWeekStore(stored: null);
            var store = new BlobFirstFinalizedWeekStore(sql, blob, NullLogger<BlobFirstFinalizedWeekStore>.Instance, DuringTheSeason);

            await store.ReadAsync(League, 2026, default);

            blob.Written.Should().NotBeNull();
            blob.Written.Should().ContainSingle();
            blob.Written![0].Ideal.Should().Be(100m);
        }

        [Fact]
        public async Task A_cold_blob_outside_the_season_is_not_primed()
        {
            // same gate as a write: don't plant a stale blob for a season
            // nobody is actively polling
            var sql = SqlStore();
            await sql.WriteAsync(League, 2026, [Week()], default);
            var blob = new FakeBlobFinalizedWeekStore(stored: null);
            var store = new BlobFirstFinalizedWeekStore(sql, blob, NullLogger<BlobFirstFinalizedWeekStore>.Instance, TheOffseason);

            await store.ReadAsync(League, 2026, default);

            blob.Written.Should().BeNull();
        }

        [Fact]
        public async Task Nothing_anywhere_reads_back_empty_rather_than_null()
        {
            var store = new BlobFirstFinalizedWeekStore(
                SqlStore(), new FakeBlobFinalizedWeekStore(), NullLogger<BlobFirstFinalizedWeekStore>.Instance, DuringTheSeason);

            (await store.ReadAsync(League, 2026, default)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_write_always_reaches_sql()
        {
            // SQL stays the durable copy regardless of the blob gate
            var sql = SqlStore();
            var store = new BlobFirstFinalizedWeekStore(
                sql, new FakeBlobFinalizedWeekStore(), NullLogger<BlobFirstFinalizedWeekStore>.Instance, TheOffseason);

            await store.WriteAsync(League, 2026, [Week()], default);

            (await sql.ReadAsync(League, 2026, default)).Should().ContainSingle();
        }

        [Fact]
        public async Task A_write_during_the_season_also_reaches_the_blob()
        {
            var blob = new FakeBlobFinalizedWeekStore();
            var store = new BlobFirstFinalizedWeekStore(
                SqlStore(), blob, NullLogger<BlobFirstFinalizedWeekStore>.Instance, DuringTheSeason);

            await store.WriteAsync(League, 2026, [Week(ideal: 155m)], default);

            blob.Written.Should().NotBeNull();
            blob.Written![0].Ideal.Should().Be(155m);
        }

        [Fact]
        public async Task A_write_during_the_offseason_skips_the_blob()
        {
            // guards against overwriting the current season's blob with a
            // stray write while nothing is actually being played
            var blob = new FakeBlobFinalizedWeekStore();
            var store = new BlobFirstFinalizedWeekStore(
                SqlStore(), blob, NullLogger<BlobFirstFinalizedWeekStore>.Instance, TheOffseason);

            await store.WriteAsync(League, 2026, [Week()], default);

            blob.Written.Should().BeNull();
        }
    }

    public class NullBlobFinalizedWeekStoreTests
    {
        [Fact]
        public async Task It_reads_back_nothing()
        {
            (await new NullBlobFinalizedWeekStore().ReadAsync("L1", 2026, default)).Should().BeNull();
        }

        [Fact]
        public async Task It_accepts_a_write_without_throwing()
        {
            await new NullBlobFinalizedWeekStore()
                .Invoking(s => s.WriteAsync("L1", 2026, [], default))
                .Should().NotThrowAsync();
        }
    }
}
