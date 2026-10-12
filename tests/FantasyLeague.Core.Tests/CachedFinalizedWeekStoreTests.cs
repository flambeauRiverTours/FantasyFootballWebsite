using FantasyLeague.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantasyLeague.Core.Tests
{
    public class CachedFinalizedWeekStoreTests
    {
        private const string League = "L1";

        private static (CachedFinalizedWeekStore Cached, CountingContextFactory Factory) Build()
        {
            var factory = new CountingContextFactory(Guid.NewGuid().ToString());

            var sql = new SqlFinalizedWeekStore(factory, TimeProvider.System, NullLogger<SqlFinalizedWeekStore>.Instance);
            var blobFirst = new BlobFirstFinalizedWeekStore(sql, new NullBlobFinalizedWeekStore());

            var cache = new ServiceCollection()
                .AddHybridCache()
                .Services
                .BuildServiceProvider()
                .GetRequiredService<HybridCache>();

            return (new CachedFinalizedWeekStore(
                blobFirst, cache, NullLogger<CachedFinalizedWeekStore>.Instance), factory);
        }

        private static TeamWeek Week(int week = 1, decimal ideal = 120m) =>
            new(1, week, 100m, ideal, [], [], [], true, new Dictionary<string, decimal>());

        [Fact]
        public async Task A_second_read_does_not_touch_the_database()
        {
            // the free tier allows roughly 55 awake hours a month; a query per
            // request would never let it pause
            var (cached, factory) = Build();
            await cached.WriteAsync(League, 2026, [Week()], TestContext.Current.CancellationToken);

            await cached.ReadAsync(League, 2026, TestContext.Current.CancellationToken);
            var afterFirst = factory.Created;
            await cached.ReadAsync(League, 2026, TestContext.Current.CancellationToken);

            factory.Created.Should().Be(afterFirst);
        }

        [Fact]
        public async Task The_cached_answer_is_the_stored_answer()
        {
            var (cached, _) = Build();
            await cached.WriteAsync(League, 2026, [Week(ideal: 133m)], TestContext.Current.CancellationToken);

            var first = await cached.ReadAsync(League, 2026, TestContext.Current.CancellationToken);
            var second = await cached.ReadAsync(League, 2026, TestContext.Current.CancellationToken);

            first.Should().BeEquivalentTo(second);
            second[0].Ideal.Should().Be(133m);
        }

        [Fact]
        public async Task A_write_drops_the_cached_answer()
        {
            // without this the finalize timer writes a week nobody can see
            var (cached, _) = Build();

            await cached.WriteAsync(League, 2026, [Week(ideal: 120m)], TestContext.Current.CancellationToken);
            (await cached.ReadAsync(League, 2026, TestContext.Current.CancellationToken))[0].Ideal.Should().Be(120m);

            await cached.WriteAsync(League, 2026, [Week(ideal: 155m)], TestContext.Current.CancellationToken);

            (await cached.ReadAsync(League, 2026, TestContext.Current.CancellationToken))[0].Ideal.Should().Be(155m);
        }

        [Fact]
        public async Task Seasons_are_cached_separately()
        {
            var (cached, _) = Build();

            await cached.WriteAsync(League, 2025, [Week(ideal: 99m)], TestContext.Current.CancellationToken);
            await cached.WriteAsync(League, 2026, [Week(ideal: 111m)], TestContext.Current.CancellationToken);

            (await cached.ReadAsync(League, 2025, TestContext.Current.CancellationToken))[0].Ideal.Should().Be(99m);
            (await cached.ReadAsync(League, 2026, TestContext.Current.CancellationToken))[0].Ideal.Should().Be(111m);
        }
    }

    internal sealed class CountingContextFactory(string name) : IDbContextFactory<FantasyDataContext>
    {
        public int Created { get; private set; }

        public FantasyDataContext CreateDbContext()
        {
            Created++;

            return new FantasyDataContext(
                new DbContextOptionsBuilder<FantasyDataContext>()
                    .UseInMemoryDatabase(name)
                    .Options);
        }
    }
}
