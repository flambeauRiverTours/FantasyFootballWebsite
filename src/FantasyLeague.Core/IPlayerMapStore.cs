namespace FantasyLeague.Core
{
    /// <summary>
    /// The slim player map as it is persisted between instances.
    /// </summary>
    /// <param name="RefreshedUtc">When the map was fetched from Sleeper.</param>
    /// <param name="Players">The projected map.</param>
    public sealed record StoredPlayerMap(
        DateTimeOffset RefreshedUtc,
        IReadOnlyDictionary<string, PlayerRef> Players);

    /// <summary>
    /// Where the slim player map lives between instances (A12).
    /// </summary>
    /// <remarks>
    /// The same seam as <see cref="IFinalizedWeekStore"/>: the domain owns the
    /// interface and the null implementation, and the host with storage wires in
    /// the real one.
    /// </remarks>
    public interface IPlayerMapStore
    {
        /// <summary>
        /// Reads the stored map.
        /// </summary>
        /// <returns>The stored map, or null when nothing has been stored or the store is unreachable.</returns>
        Task<StoredPlayerMap?> ReadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Replaces the stored map.
        /// </summary>
        /// <param name="map">The map to store.</param>
        /// <returns>A task that completes when the map has been written.</returns>
        Task WriteAsync(StoredPlayerMap map, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The store used when no storage account is configured.
    /// </summary>
    public sealed class NullPlayerMapStore : IPlayerMapStore
    {
        /// <inheritdoc />
        public Task<StoredPlayerMap?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<StoredPlayerMap?>(null);

        /// <inheritdoc />
        public Task WriteAsync(StoredPlayerMap map, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
