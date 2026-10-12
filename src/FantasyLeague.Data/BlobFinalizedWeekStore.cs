using System.Text.Json;
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
        public async Task<IReadOnlyList<TeamWeek>?> ReadAsync(string leagueId, int season, CancellationToken ct)
        {
            try
            {
                var stored = await ReadStoredAsync(leagueId, ct);
                return stored?.Season == season ? stored.Weeks : null;
            }
            catch (RequestFailedException ex) when (IsTransient(ex))
            {
                logger.LogWarning(ex, "Could not read the stored season; falling back to SQL.");
                return null;
            }
        }

        /// <inheritdoc />
        public async Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken ct)
        {
            if (weeks.Count == 0)
            {
                logger.LogWarning("Skipped storing season {Season}: there are no weeks to store.", season);
                return;
            }

            try
            {
                var stored = await ReadStoredAsync(leagueId, ct);
                if (stored is not null && stored.Season > season)
                {
                    // a backfill of an older season must not evict the live one
                    logger.LogInformation("Skipped storing season {Season}: the blob holds the newer {StoredSeason}.", season, stored.Season);
                    return;
                }

                await container.CreateIfNotExistsAsync(cancellationToken: ct);

                using var buffer = new MemoryStream();
                await JsonSerializer.SerializeAsync(buffer, new StoredSeason(season, weeks), cancellationToken: ct);
                buffer.Position = 0;

                await container.GetBlobClient(BlobName(leagueId)).UploadAsync(buffer, overwrite: true, ct);
            }
            catch (RequestFailedException ex) when (IsTransient(ex))
            {
                logger.LogWarning(ex, "Could not store the finalized season; the next read falls back to SQL.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not store the finalized season; the next read falls back to SQL.");
            }
        }

        /// <summary>
        /// Names the league's one blob.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <returns>The blob name.</returns>
        private static string BlobName(string leagueId) => $"{leagueId}/current.json";

        /// <summary>
        /// Decides whether a storage failure could succeed if tried again.
        /// </summary>
        /// <param name="ex">The storage failure.</param>
        /// <returns><c>true</c> for no response, a timeout, throttling or a server error.</returns>
        private static bool IsTransient(RequestFailedException ex) => ex.Status is 0 or 408 or 429 or >= 500;

        /// <summary>
        /// Reads whatever season the blob holds.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="ct">CancellationToken.</param>
        /// <returns>The stored season, or null when nothing has been stored.</returns>
        private async Task<StoredSeason?> ReadStoredAsync(string leagueId, CancellationToken ct)
        {
            try
            {
                var response = await container.GetBlobClient(BlobName(leagueId)).DownloadStreamingAsync(cancellationToken: ct);
                return await JsonSerializer.DeserializeAsync<StoredSeason>(response.Value.Content, cancellationToken: ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }
    }
}
