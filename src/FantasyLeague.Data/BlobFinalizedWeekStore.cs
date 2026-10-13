using System.Security.Cryptography;
using System.Text;
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
    /// <param name="WrittenUtc">When the blob was written. A blob older than the last finalize run misses.</param>
    /// <param name="Weeks">The season's finalized weeks.</param>
    internal sealed record StoredSeason(int Season, DateTimeOffset WrittenUtc, IReadOnlyList<TeamWeek> Weeks);

    /// <summary>
    /// Caches the current season's finalized weeks in one blob per league (A20).
    /// </summary>
    /// <remarks>
    /// Only the newest season it has been given is kept. SQL holds every season,
    /// so a past season is never read from here.
    /// </remarks>
    /// <param name="container">The container the blob lives in.</param>
    /// <param name="time">Clock for the write time and the staleness check.</param>
    internal sealed class BlobFinalizedWeekStore(
        BlobContainerClient container,
        TimeProvider time,
        ILogger<BlobFinalizedWeekStore> logger)
    {
        /// <summary>
        /// Reads the stored season.
        /// </summary>
        /// <param name="season">The season wanted.</param>
        /// <returns>The season's weeks, or null on a miss or a transient failure.</returns>
        /// <remarks>
        /// A blob written before the last finalize run misses, so the caller
        /// reads SQL and rewrites it. That is the case where Tuesday's job
        /// reached SQL but its blob write failed, which would otherwise hide the
        /// new week until next Tuesday.
        /// </remarks>
        public async Task<IReadOnlyList<TeamWeek>?> ReadAsync(string leagueId, int season, CancellationToken cancellationToken)
        {
            try
            {
                var stored = await BlobJson.ReadAsync<StoredSeason>(container.GetBlobClient(BlobName(leagueId)), cancellationToken);
                if (stored?.Season != season)
                {
                    return null;
                }

                if (stored.WrittenUtc < FinalizeSchedule.LastRunAtOrBefore(time.GetUtcNow()))
                {
                    logger.LogInformation("The stored season was written at {WrittenUtc}, before the last finalize run; reading SQL instead.", stored.WrittenUtc);
                    return null;
                }

                return stored.Weeks;
            }
            catch (RequestFailedException ex) when (BlobJson.IsTransient(ex))
            {
                logger.LogWarning(ex, "Could not read the stored season; falling back to SQL.");
                return null;
            }
        }

        /// <summary>
        /// Stores a season, unless the blob already holds a newer one. Never throws
        /// for a storage failure: SQL is the record, and the next read falls back to it.
        /// </summary>
        /// <param name="season">The season the weeks belong to.</param>
        /// <param name="weeks">The season's finalized weeks.</param>
        /// <returns>A task that completes when the blob is written or skipped.</returns>
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

                await BlobJson.WriteAsync(container, BlobName(leagueId), new StoredSeason(season, time.GetUtcNow(), weeks), cancellationToken);
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
        private const int Schema = 4;

        /// <summary>
        /// Names the league's one blob.
        /// </summary>
        /// <returns>The blob name, keyed by a hash of the league rather than its id.</returns>
        /// <remarks>
        /// Blob names show in the portal and in storage logs, and the league id
        /// is a secret (A2). The hash keeps leagues apart without naming one; it
        /// hides the id from a casual reader, not from someone who already
        /// suspects which league it is.
        /// </remarks>
        private static string BlobName(string leagueId) =>
            $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(leagueId)))[..16]}/current.v{Schema}.json";
    }
}
