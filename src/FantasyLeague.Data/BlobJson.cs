using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;

namespace FantasyLeague.Data
{
    /// <summary>
    /// The read, write and failure rules both blob stores share.
    /// </summary>
    internal static class BlobJson
    {
        /// <summary>
        /// Reads a blob as JSON.
        /// </summary>
        /// <typeparam name="T">The stored shape.</typeparam>
        /// <param name="blob">The blob.</param>
        /// <returns>The stored value, or null when the blob does not exist.</returns>
        public static async Task<T?> ReadAsync<T>(BlobClient blob, CancellationToken cancellationToken)
            where T : class
        {
            try
            {
                var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
                return await JsonSerializer.DeserializeAsync<T>(response.Value.Content, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        /// <summary>
        /// Replaces a blob with a value written as JSON, creating its container if needed.
        /// </summary>
        /// <typeparam name="T">The stored shape.</typeparam>
        /// <param name="container">The container.</param>
        /// <param name="blobName">The blob's name.</param>
        /// <param name="value">The value to store.</param>
        /// <returns>The number of bytes written.</returns>
        public static async Task<long> WriteAsync<T>(BlobContainerClient container, string blobName, T value, CancellationToken cancellationToken)
        {
            await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            using var buffer = new MemoryStream();
            await JsonSerializer.SerializeAsync(buffer, value, cancellationToken: cancellationToken);
            buffer.Position = 0;

            await container.GetBlobClient(blobName).UploadAsync(buffer, overwrite: true, cancellationToken);
            return buffer.Length;
        }

        /// <summary>
        /// Decides whether a storage failure could succeed if tried again.
        /// </summary>
        /// <param name="ex">The storage failure.</param>
        /// <returns><c>true</c> for no response, a timeout, throttling or a server error.</returns>
        public static bool IsTransient(RequestFailedException ex) => ex.Status is 0 or 408 or 429 or >= 500;
    }
}
