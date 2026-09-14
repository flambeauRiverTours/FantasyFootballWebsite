using FantasyLeague.Core;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Cache in front of the current week, to avoid successive db trips
    /// </summary>
    /// <param name="inner">DB object for the finalized week</param>
    /// <param name="cache">Hybrid cache object</param>
    /// <param name="logger"></param>
    internal sealed class CachedFinalizedWeekStore(SqlFinalizedWeekStore inner, HybridCache cache,
        ILogger<CachedFinalizedWeekStore> logger) : IFinalizedWeekStore
    {
        private static readonly HybridCacheEntryOptions Finalized = new() { Expiration = TimeSpan.FromHours(6) };

        /// <summary>
        /// Reads from the cache
        /// </summary>
        /// <param name="leagueId">League id to read from</param>
        /// <param name="season">Season to read from</param>
        /// <param name="cancellationToken">CT for the trip</param>
        /// <returns>List of teamweeks in the cache/otehrwise read from db</returns>
        public async Task<IReadOnlyList<TeamWeek>> ReadAsync(string leagueId, int season,
            CancellationToken cancellationToken) =>
            await cache.GetOrCreateAsync($"finalized:{leagueId}:{season}",
                ct => new ValueTask<IReadOnlyList<TeamWeek>>(inner.ReadAsync(leagueId, season, ct)),
                Finalized, tags: [Tag(leagueId)], cancellationToken: cancellationToken);

        /// <summary>
        /// Writes to the caches
        /// </summary>
        /// <param name="leagueId">League to write to</param>
        /// <param name="season">Season to write to</param>
        /// <param name="weeks">List of weeks to write</param>
        /// <param name="cancellationToken">CT for the await operations</param>
        /// <returns>Nothing</returns>
        public async Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks,
            CancellationToken cancellationToken)
        {
            await inner.WriteAsync(leagueId, season, weeks, cancellationToken);

            await cache.RemoveByTagAsync(Tag(leagueId), cancellationToken);

            logger.LogInformation("Dropped cached weeks for league after a write.");
        }

        private static string Tag(string leagueId) => $"finalized:{leagueId}";
    }
}
