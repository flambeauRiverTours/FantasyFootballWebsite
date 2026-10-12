using Azure;
using Azure.Storage.Blobs;
using FantasyLeague.Core;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Data
{
    /// <summary>
    /// The one season the blob holds.
    /// </summary>
    /// <param name="Season">The season the weeks belong to. A read for any other season misses.</param>
    /// <param name="Weeks">The season's finalized weeks.</param>
    internal sealed record StoredSeason(int Season, IReadOnlyList<TeamWeek> Weeks);

    /// <summary>
    /// Caches the current season's finalized weeks in one blob per league (A20).
    /// </summary>
    /// <remarks>
    /// Only the newest season it has been given is kept. SQL holds every season,
    /// so a past season is never read from here.
    /// </remarks>
    /// <param name="container">The container the blob lives in.</param>
    /// <param name="logger">Logger.</param>
    internal sealed class BlobFinalizedWeekStore(
        BlobContainerClient container,
        ILogger<BlobFinalizedWeekStore> logger) : IBlobFinalizedWeekStore
    {
        /// <inheritdoc />
        public async Task<IReadOnlyList<TeamWeek>?> ReadAsync(string leagueId, int season, CancellationToken cancellationToken)
        {
            try
            {
                var stored = await BlobJson.ReadAsync<StoredSeason>(container.GetBlobClient(BlobName(leagueId)), cancellationToken);
                return stored?.Season == season ? stored.Weeks : null;
            }
            catch (RequestFailedException ex) when (BlobJson.IsTransient(ex))
            {
                logger.LogWarning(ex, "Could not read the stored season; falling back to SQL.");
                return null;
            }
        }

        /// <inheritdoc />
        public async Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken cancellationToken)
        {
            if (weeks.Count == 0)
            {
                logger.LogWarning("Skipped storing season {Season}: there are no weeks to store.", season);
                return;
            }

            try
            {
                var stored = await BlobJson.ReadAsync<StoredSeason>(container.GetBlobClient(BlobName(leagueId)), cancellationToken);
                if (stored is not null && stored.Season > season)
                {
                    // a backfill of an older season must not evict the live one
                    logger.LogInformation("Skipped storing season {Season}: the blob holds the newer {StoredSeason}.", season, stored.Season);
                    return;
                }

                await BlobJson.WriteAsync(container, BlobName(leagueId), new StoredSeason(season, weeks), cancellationToken);
            }
            catch (RequestFailedException ex) when (BlobJson.IsTransient(ex))
            {
                logger.LogWarning(ex, "Could not store the finalized season; the next read falls back to SQL.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not store the finalized season; the next read falls back to SQL.");
            }
        }

        /// <summary>
        /// The stored shape's version. Bump it whenever <see cref="TeamWeek"/>
        /// changes, so an old blob is a cache miss rather than a misread.
        /// </summary>
        private const int Schema = 2;

        /// <summary>
        /// Names the league's one blob.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <returns>The blob name.</returns>
        private static string BlobName(string leagueId) => $"{leagueId}/current.v{Schema}.json";
    }
}
