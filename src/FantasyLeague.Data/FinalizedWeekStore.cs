using System.ComponentModel;
using FantasyLeague.Core;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Data
{
    /// <summary>
    /// The finalized-week store the host uses: memory first, then the current
    /// season's blob, then SQL, which holds every season (A16, A20).
    /// </summary>
    /// <param name="sql">Every season's finalized weeks.</param>
    /// <param name="blob">The current season only. It decides for itself which season that is.</param>
    /// <param name="cache">The in-memory cache.</param>
    /// <param name="logger">Logger.</param>
    internal sealed class FinalizedWeekStore(
        SqlFinalizedWeekStore sql,
        BlobFinalizedWeekStore blob,
        HybridCache cache,
        ILogger<FinalizedWeekStore> logger) : IFinalizedWeekStore
    {
        /// <summary>
        /// Finalized weeks change once a week, and every write clears the cache.
        /// </summary>
        private static readonly HybridCacheEntryOptions Finalized = new() { Expiration = TimeSpan.FromHours(6) };

        /// <inheritdoc />
        /// <remarks>
        /// A database still waking after its retries reads back empty, so the
        /// season is rebuilt from Sleeper rather than the request failing. The
        /// failure throws inside the cache's factory, so it is never cached:
        /// an empty answer held for six hours would rebuild every finalized
        /// week from Sleeper long after the database came back, and by mid
        /// season that is more than the rate limit allows.
        /// </remarks>
        public async Task<IReadOnlyList<TeamWeek>> ReadAsync(string leagueId, int season, CancellationToken cancellationToken)
        {
            try
            {
                var entry = await cache.GetOrCreateAsync(
                    $"finalized:{leagueId}:{season}",
                    async token => new CachedWeeks(await ReadThroughAsync(leagueId, season, token)),
                    Finalized,
                    tags: [Tag(leagueId)],
                    cancellationToken: cancellationToken);

                return entry.Weeks;
            }
            catch (Exception ex) when (ex is RetryLimitExceededException or TimeoutException)
            {
                logger.LogWarning(ex, "Could not read stored weeks for season {Season}; rebuilding from Sleeper.", season);
                return [];
            }
        }

        /// <inheritdoc />
        public async Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken cancellationToken)
        {
            await sql.WriteAsync(leagueId, season, weeks, cancellationToken);
            await blob.WriteAsync(leagueId, season, weeks, cancellationToken);
            await cache.RemoveByTagAsync(Tag(leagueId), cancellationToken);

            logger.LogInformation("Stored season {Season} and dropped its cached weeks.", season);
        }

        /// <summary>
        /// Reads the blob, falling back to SQL and priming the blob with what SQL held.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="season">The season.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The season's finalized weeks, empty when none are stored.</returns>
        private async Task<IReadOnlyList<TeamWeek>> ReadThroughAsync(string leagueId, int season, CancellationToken cancellationToken)
        {
            if (await blob.ReadAsync(leagueId, season, cancellationToken) is { } fromBlob)
            {
                return fromBlob;
            }

            var fromSql = await sql.ReadAsync(leagueId, season, cancellationToken);

            // primes a cold or stale blob rather than waiting for Tuesday
            await blob.WriteAsync(leagueId, season, fromSql, cancellationToken);

            return fromSql;
        }

        /// <summary>
        /// Tags every cached season of a league, so one write clears them all.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <returns>The tag.</returns>
        private static string Tag(string leagueId) => $"finalized:{leagueId}";

        /// <summary>
        /// A cached season, marked immutable so HybridCache hands back the same
        /// instance rather than deserializing a copy on every hit.
        /// </summary>
        /// <param name="Weeks">The season's finalized weeks.</param>
        [ImmutableObject(true)]
        private sealed record CachedWeeks(IReadOnlyList<TeamWeek> Weeks);
    }
}
