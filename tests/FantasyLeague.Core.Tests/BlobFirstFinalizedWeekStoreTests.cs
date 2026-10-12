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

    public class BlobFirstFinalizedWeekStoreTests
    {
        private const string League = "L1";

        private static SqlFinalizedWeekStore SqlStore() =>
            new(new InMemoryContextFactory(Guid.NewGuid().ToString()), TimeProvider.System, NullLogger<SqlFinalizedWeekStore>.Instance);

        private static TeamWeek Week(decimal ideal = 120m) =>
            new(1, 1, 100m, ideal, [], [], [], true, new Dictionary<string, decimal>());

        [Fact]
        public async Task A_read_prefers_the_blob_over_sql()
        {
            var sql = SqlStore();
            await sql.WriteAsync(League, 2026, [Week(ideal: 100m)], TestContext.Current.CancellationToken);
            var store = new BlobFirstFinalizedWeekStore(sql, new FakeBlobFinalizedWeekStore([Week(ideal: 999m)]));

            var read = await store.ReadAsync(League, 2026, TestContext.Current.CancellationToken);

            read.Should().ContainSingle();
            read[0].Ideal.Should().Be(999m);
        }

        [Fact]
        public async Task A_cold_blob_falls_through_to_sql()
        {
            var sql = SqlStore();
            await sql.WriteAsync(League, 2026, [Week(ideal: 100m)], TestContext.Current.CancellationToken);
            var store = new BlobFirstFinalizedWeekStore(sql, new FakeBlobFinalizedWeekStore(stored: null));

            var read = await store.ReadAsync(League, 2026, TestContext.Current.CancellationToken);

            read.Should().ContainSingle();
            read[0].Ideal.Should().Be(100m);
        }

        [Fact]
        public async Task A_cold_blob_is_offered_what_sql_returned()
        {
            // T58: otherwise the blob sits empty from deploy until the first
            // Tuesday finalize run. Whether it keeps it is the blob's decision.
            var sql = SqlStore();
            await sql.WriteAsync(League, 2026, [Week(ideal: 100m)], TestContext.Current.CancellationToken);
            var blob = new FakeBlobFinalizedWeekStore(stored: null);
            var store = new BlobFirstFinalizedWeekStore(sql, blob);

            await store.ReadAsync(League, 2026, TestContext.Current.CancellationToken);

            blob.Written.Should().ContainSingle().Which.Ideal.Should().Be(100m);
        }

        [Fact]
        public async Task Nothing_anywhere_reads_back_empty_rather_than_null()
        {
            var store = new BlobFirstFinalizedWeekStore(SqlStore(), new FakeBlobFinalizedWeekStore());

            (await store.ReadAsync(League, 2026, TestContext.Current.CancellationToken)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_write_reaches_sql_and_the_blob()
        {
            var sql = SqlStore();
            var blob = new FakeBlobFinalizedWeekStore();
            var store = new BlobFirstFinalizedWeekStore(sql, blob);

            await store.WriteAsync(League, 2026, [Week(ideal: 155m)], TestContext.Current.CancellationToken);

            (await sql.ReadAsync(League, 2026, TestContext.Current.CancellationToken)).Should().ContainSingle();
            blob.Written.Should().ContainSingle().Which.Ideal.Should().Be(155m);
        }
    }

    /// <summary>
    /// A20: the blob holds one season, the newest it has been given.
    /// </summary>
    public class BlobFinalizedWeekStoreTests
    {
        private const string League = "L1";

        private static TeamWeek Week(decimal ideal) =>
            new(1, 1, 100m, ideal, [], [], [], true, new Dictionary<string, decimal>());

        private static BlobFinalizedWeekStore Store(MemoryContainer container) =>
            new(container, NullLogger<BlobFinalizedWeekStore>.Instance);

        [Fact]
        public async Task A_written_season_reads_back()
        {
            var store = Store(new MemoryContainer());
            await store.WriteAsync(League, 2026, [Week(110m)], TestContext.Current.CancellationToken);

            var read = await store.ReadAsync(League, 2026, TestContext.Current.CancellationToken);

            read.Should().ContainSingle().Which.Ideal.Should().Be(110m);
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
                .Which.Ideal.Should().Be(110m);
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
        public async Task An_empty_season_is_not_stored()
        {
            var container = new MemoryContainer();

            await Store(container).WriteAsync(League, 2026, [], TestContext.Current.CancellationToken);

            container.Blobs.Should().BeEmpty();
        }
    }

    public class NullBlobFinalizedWeekStoreTests
    {
        [Fact]
        public async Task It_reads_back_nothing() => (await new NullBlobFinalizedWeekStore().ReadAsync("L1", 2026, TestContext.Current.CancellationToken)).Should().BeNull();

        [Fact]
        public async Task It_accepts_a_write_without_throwing()
        {
            await new NullBlobFinalizedWeekStore()
                .Invoking(s => s.WriteAsync("L1", 2026, [], default))
                .Should().NotThrowAsync();
        }
    }
}
