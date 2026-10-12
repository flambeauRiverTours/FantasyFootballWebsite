using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// The slim player map as it is persisted between instances.
    /// </summary>
    /// <param name="RefreshedUtc">When the map was fetched from Sleeper.</param>
    /// <param name="Players">The projected map.</param>
    internal sealed record StoredPlayerMap(
        DateTimeOffset RefreshedUtc,
        IReadOnlyDictionary<string, PlayerRef> Players);

    /// <summary>
    /// Where the slim player map lives between instances.
    /// </summary>
    internal interface IPlayerMapStore
    {
        /// <summary>
        /// Reads the stored map.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The stored map, or null when nothing has been stored or the store is unreachable.</returns>
        Task<StoredPlayerMap?> ReadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Replaces the stored map.
        /// </summary>
        /// <param name="map">The map to store.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>A task that completes when the map has been written.</returns>
        Task WriteAsync(StoredPlayerMap map, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The store used when no storage account is configured.
    /// </summary>
    internal sealed class NullPlayerMapStore : IPlayerMapStore
    {
        /// <inheritdoc />
        public Task<StoredPlayerMap?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<StoredPlayerMap?>(null);

        /// <inheritdoc />
        public Task WriteAsync(StoredPlayerMap map, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Keeps the slim map in a blob alongside the function's own runtime state.
    /// </summary>
    /// <param name="container">The container the blob lives in.</param>
    /// <param name="blobName">The blob name.</param>
    /// <param name="logger">Logger.</param>
    internal sealed class BlobPlayerMapStore(
        BlobContainerClient container,
        string blobName,
        ILogger<BlobPlayerMapStore> logger) : IPlayerMapStore
    {
        /// <summary>Written and read by this application only, so nothing Sleeper-shaped applies.</summary>
        private static readonly JsonSerializerOptions Json = new();

        /// <inheritdoc />
        public async Task<StoredPlayerMap?> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await container.GetBlobClient(blobName)
                    .DownloadStreamingAsync(cancellationToken: cancellationToken);

                return await JsonSerializer.DeserializeAsync<StoredPlayerMap>(
                    response.Value.Content, Json, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                logger.LogInformation("No stored player map yet.");
                return null;
            }

            catch (RequestFailedException ex) when (IsTransient(ex))
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
                await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

                using var buffer = new MemoryStream();
                await JsonSerializer.SerializeAsync(buffer, map, Json, cancellationToken);
                buffer.Position = 0;

                await container.GetBlobClient(blobName).UploadAsync(buffer, overwrite: true, cancellationToken);

                logger.LogInformation("Stored the player map. Startable: {StartableCount}, bytes: {Bytes}.", map.Players.Count, buffer.Length);
            }
            catch (RequestFailedException ex) when (IsTransient(ex))
            {
                logger.LogWarning(ex, "Could not store the player map; the next cold instance will refetch.");
            }

            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not store the player map; the next cold instance will refetch.");
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
