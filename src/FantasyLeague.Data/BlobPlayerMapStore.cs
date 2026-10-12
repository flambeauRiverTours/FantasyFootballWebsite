using Azure;
using Azure.Storage.Blobs;
using FantasyLeague.Core;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Keeps the slim map in a blob alongside the function's own runtime state (A12).
    /// </summary>
    /// <param name="container">The container the blob lives in.</param>
    /// <param name="blobName">The blob name.</param>
    /// <param name="logger">Logger.</param>
    internal sealed class BlobPlayerMapStore(
        BlobContainerClient container,
        string blobName,
        ILogger<BlobPlayerMapStore> logger) : IPlayerMapStore
    {
        /// <inheritdoc />
        public async Task<StoredPlayerMap?> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                var stored = await BlobJson.ReadAsync<StoredPlayerMap>(container.GetBlobClient(blobName), cancellationToken);
                if (stored is null)
                {
                    logger.LogInformation("No stored player map yet.");
                }

                return stored;
            }
            catch (RequestFailedException ex) when (BlobJson.IsTransient(ex))
            {
                logger.LogWarning(ex, "Could not read the stored player map; falling back to Sleeper.");
                return null;
            }
        }

        /// <inheritdoc />
        public async Task WriteAsync(StoredPlayerMap map, CancellationToken cancellationToken)
        {
            try
            {
                var bytes = await BlobJson.WriteAsync(container, blobName, map, cancellationToken);

                logger.LogInformation("Stored the player map. Startable: {StartableCount}, bytes: {Bytes}.", map.Players.Count, bytes);
            }
            catch (RequestFailedException ex) when (BlobJson.IsTransient(ex))
            {
                logger.LogWarning(ex, "Could not store the player map; the next cold instance will refetch.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not store the player map; the next cold instance will refetch.");
            }
        }
    }
}
