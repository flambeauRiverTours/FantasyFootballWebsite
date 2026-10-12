using FantasyLeague.Core;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Keeps each season's finalized weeks in memory in front of the blob and SQL.
    /// </summary>
    /// <param name="inner">The blob-first store behind it.</param>
    /// <param name="cache">The in-memory cache.</param>
    /// <param name="logger">Logger.</param>
    internal sealed class CachedFinalizedWeekStore(
        BlobFirstFinalizedWeekStore inner,
        HybridCache cache,
        ILogger<CachedFinalizedWeekStore> logger) : IFinalizedWeekStore
    {
        /// <summary>
        /// Finalized weeks change once a week, and every write clears the cache.
        /// </summary>
        private static readonly HybridCacheEntryOptions Finalized = new() { Expiration = TimeSpan.FromHours(6) };

        /// <inheritdoc />
        public async Task<IReadOnlyList<TeamWeek>> ReadAsync(string leagueId, int season,
            CancellationToken cancellationToken) =>
            await cache.GetOrCreateAsync($"finalized:{leagueId}:{season}",
                token => new ValueTask<IReadOnlyList<TeamWeek>>(inner.ReadAsync(leagueId, season, token)),
                Finalized, tags: [Tag(leagueId)], cancellationToken: cancellationToken);

        /// <inheritdoc />
        public async Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks,
            CancellationToken cancellationToken)
        {
            await inner.WriteAsync(leagueId, season, weeks, cancellationToken);

            await cache.RemoveByTagAsync(Tag(leagueId), cancellationToken);

            logger.LogInformation("Dropped cached weeks for league after a write.");
        }

        /// <summary>
        /// Tags every cached season of a league, so one write clears them all.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <returns>The tag.</returns>
        private static string Tag(string leagueId) => $"finalized:{leagueId}";
    }
}
