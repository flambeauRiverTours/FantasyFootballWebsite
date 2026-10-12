using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using FantasyLeague.Core;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Represents a blob-stored list of weeks
    /// </summary>
    /// <param name="FinalizedUtc"></param>
    /// <param name="Weeks"></param>
    internal sealed record StoredSeason(DateTimeOffset FinalizedUtc, IReadOnlyList<TeamWeek> Weeks);

    /// <summary>
    /// Handles the blob store of the cached current season
    /// </summary>
    /// <param name="container">Blob container stoed in</param>
    /// <param name="time">Clock for the stored timestamp</param>
    /// <param name="logger">Logger to use</param>
    internal sealed class BlobFinalizedWeekStore(
    BlobContainerClient container,
    TimeProvider time,
    ILogger<BlobFinalizedWeekStore> logger) : IBlobFinalizedWeekStore
    {
        // one blob per (leagueId, season) — one blob in practice, per Scope above
        private static string BlobName(string leagueId, int season) => $"{leagueId}/{season}.json";

        /// <inheritdoc />
        public async Task<IReadOnlyList<TeamWeek>?> ReadAsync(string leagueId, int season, CancellationToken ct)
        {
            try
            {
                var response = await container.GetBlobClient(BlobName(leagueId, season))
                    .DownloadStreamingAsync(cancellationToken: ct);
                var stored = await JsonSerializer.DeserializeAsync<StoredSeason>(response.Value.Content, cancellationToken: ct);
                return stored?.Weeks;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null; // cold or never-written — caller falls back to SQL
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
            if ((weeks == null) || (weeks.Count == 0))
            {
                logger.LogWarning("Tried to write to BlobFinalizedWeekStore with an empty list of weeks");
                return;
            }
            try
            {
                await container.CreateIfNotExistsAsync(cancellationToken: ct);
                using var buffer = new MemoryStream();
                await JsonSerializer.SerializeAsync(buffer, new StoredSeason(time.GetUtcNow(), weeks), cancellationToken: ct);
                buffer.Position = 0;
                await container.GetBlobClient(BlobName(leagueId, season)).UploadAsync(buffer, overwrite: true, ct);
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
        /// Decides whether a storage failure could succeed if tried again.
        /// </summary>
        /// <param name="ex">The storage failure.</param>
        /// <returns><c>true</c> for no response, a timeout, throttling or a server error.</returns>
        private static bool IsTransient(RequestFailedException ex) => ex.Status is 0 or 408 or 429 or >= 500;
    }
}
