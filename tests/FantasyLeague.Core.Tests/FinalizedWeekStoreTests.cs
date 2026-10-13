using FantasyLeague.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// The store the host uses: memory, then the blob, then SQL (A16, A20).
    /// </summary>
    public class FinalizedWeekStoreTests
    {
        private const string League = "L1";

        private sealed record Parts(
            FinalizedWeekStore Store,
            SqlFinalizedWeekStore Sql,
            BlobFinalizedWeekStore Blob,
            MemoryContainer Container,
            CountingContextFactory Factory);

        private static Parts Build()
        {
            var factory = new CountingContextFactory(Guid.NewGuid().ToString());
            var container = new MemoryContainer();
            var sql = new SqlFinalizedWeekStore(factory, TimeProvider.System, NullLogger<SqlFinalizedWeekStore>.Instance);
            var blob = new BlobFinalizedWeekStore(container, TimeProvider.System, NullLogger<BlobFinalizedWeekStore>.Instance);

            var cache = new ServiceCollection()
                .AddHybridCache()
                .Services
                .BuildServiceProvider()
                .GetRequiredService<HybridCache>();

            return new Parts(
                new FinalizedWeekStore(sql, blob, cache, NullLogger<FinalizedWeekStore>.Instance),
                sql, blob, container, factory);
        }

        private static TeamWeek Week(int week = 1, decimal ideal = 120m) =>
            new(1, week, 100m, ideal, [], [], [], true, new Dictionary<string, decimal>());

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_read_prefers_the_blob_over_sql()
        {
            var parts = Build();
            await parts.Sql.WriteAsync(League, 2026, [Week(ideal: 100m)], Token);
            await parts.Blob.WriteAsync(League, 2026, [Week(ideal: 999m)], Token);

            (await parts.Store.ReadAsync(League, 2026, Token)).Should().ContainSingle()
                .Which.IdealPts.Should().Be(999m);
        }

        [Fact]
        public async Task A_cold_blob_falls_through_to_sql_and_is_primed_with_it()
        {
            // T58: otherwise the blob sits empty from deploy until the first Tuesday run
            var parts = Build();
            await parts.Sql.WriteAsync(League, 2026, [Week(ideal: 100m)], Token);

            (await parts.Store.ReadAsync(League, 2026, Token)).Should().ContainSingle()
                .Which.IdealPts.Should().Be(100m);
            (await parts.Blob.ReadAsync(League, 2026, Token)).Should().ContainSingle();
        }

        [Fact]
        public async Task Nothing_anywhere_reads_back_empty_rather_than_null() =>
            (await Build().Store.ReadAsync(League, 2026, Token)).Should().BeEmpty();

        [Fact]
        public async Task A_write_reaches_sql_and_the_blob()
        {
            var parts = Build();

            await parts.Store.WriteAsync(League, 2026, [Week(ideal: 155m)], Token);

            (await parts.Sql.ReadAsync(League, 2026, Token)).Should().ContainSingle();
            (await parts.Blob.ReadAsync(League, 2026, Token)).Should().ContainSingle()
                .Which.IdealPts.Should().Be(155m);
        }

        [Fact]
        public async Task A_second_read_touches_neither_store()
        {
            // the free tier allows roughly 55 awake hours a month; a query per
            // request would never let it pause
            var parts = Build();
            await parts.Sql.WriteAsync(League, 2026, [Week()], Token);

            await parts.Store.ReadAsync(League, 2026, Token);
            var afterFirst = parts.Factory.Created;
            parts.Container.Blobs.Clear();
            await parts.Store.ReadAsync(League, 2026, Token);

            parts.Factory.Created.Should().Be(afterFirst);
            parts.Container.Blobs.Should().BeEmpty("nothing re-primed the blob");
        }

        [Fact]
        public async Task A_cached_read_is_the_same_instance_not_a_copy()
        {
            // HybridCache deserializes a copy of anything it cannot see is immutable
            var parts = Build();
            await parts.Store.WriteAsync(League, 2026, [Week()], Token);

            var first = await parts.Store.ReadAsync(League, 2026, Token);
            var second = await parts.Store.ReadAsync(League, 2026, Token);

            second.Should().BeSameAs(first);
        }

        [Fact]
        public async Task A_write_drops_the_cached_answer()
        {
            // without this the finalize timer writes a week nobody can see
            var parts = Build();
            await parts.Store.WriteAsync(League, 2026, [Week(ideal: 120m)], Token);
            (await parts.Store.ReadAsync(League, 2026, Token))[0].IdealPts.Should().Be(120m);

            await parts.Store.WriteAsync(League, 2026, [Week(ideal: 155m)], Token);

            (await parts.Store.ReadAsync(League, 2026, Token))[0].IdealPts.Should().Be(155m);
        }

        [Fact]
        public async Task Seasons_are_cached_separately()
        {
            // the blob keeps only the newest season, so 2025 comes from SQL
            var parts = Build();
            await parts.Store.WriteAsync(League, 2025, [Week(ideal: 99m)], Token);
            await parts.Store.WriteAsync(League, 2026, [Week(ideal: 111m)], Token);

            (await parts.Store.ReadAsync(League, 2025, Token))[0].IdealPts.Should().Be(99m);
            (await parts.Store.ReadAsync(League, 2026, Token))[0].IdealPts.Should().Be(111m);
        }

        [Fact]
        public async Task An_unreachable_database_reads_back_empty_rather_than_throwing()
        {
            // the season is rebuilt from Sleeper: correct, just slower
            var parts = Build();
            parts.Factory.Failure = new RetryLimitExceededException("still paused", new TimeoutException());

            (await parts.Store.ReadAsync(League, 2026, Token)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_failed_read_is_not_cached()
        {
            // cached for six hours, the empty answer would rebuild every
            // finalized week from Sleeper long after the database woke
            var parts = Build();
            await parts.Sql.WriteAsync(League, 2026, [Week()], Token);
            parts.Factory.Failure = new RetryLimitExceededException("still paused", new TimeoutException());
            await parts.Store.ReadAsync(League, 2026, Token);

            parts.Factory.Failure = null;

            (await parts.Store.ReadAsync(League, 2026, Token)).Should().ContainSingle();
        }

        [Fact]
        public async Task A_non_transient_read_failure_still_throws()
        {
            var parts = Build();
            parts.Factory.Failure = new InvalidOperationException("Invalid column name");

            await parts.Store.Invoking(s => s.ReadAsync(League, 2026, default))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }

    internal sealed class CountingContextFactory(string name) : IDbContextFactory<FantasyDataContext>
    {
        public int Created { get; private set; }

        public Exception? Failure { get; set; }

        public FantasyDataContext CreateDbContext()
        {
            Created++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return new FantasyDataContext(
                new DbContextOptionsBuilder<FantasyDataContext>()
                    .UseInMemoryDatabase(name)
                    .Options);
        }
    }
}
