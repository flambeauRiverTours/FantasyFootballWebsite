using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Holds the projected player map and replaces it on demand.
    /// </summary>
    /// <remarks>The public seam. The implementation and its dependencies stay internal.</remarks>
    public interface IPlayerMapRefresher
    {
        /// <summary>
        /// Gets the current mapping of player IDs to PlayerRef objects.
        /// </summary>
        IReadOnlyDictionary<string, PlayerRef> PlayerMap { get; }

        /// <summary>
        /// Gets when the map was last replaced, or null if it has never been refreshed.
        /// </summary>
        DateTimeOffset? LastRefreshedUtc { get; }

        /// <summary>
        /// Refreshes the PlayerMap by fetching the latest player data from the Sleeper API.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>A task that completes when the map has been replaced.</returns>
        Task RefreshAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Makes sure a map no older than the configured maximum age is loaded,
        /// reading the store before falling back to Sleeper.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>A task that completes when a usable map is in memory.</returns>
        Task EnsureCurrentAsync(CancellationToken cancellationToken);
    }

    internal sealed class PlayerMapRefresher(
        SleeperClient client,
        IPlayerMapStore store,
        IOptions<PlayerMapOptions> options,
        ILogger<PlayerMapRefresher> logger) : IPlayerMapRefresher
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        private volatile IReadOnlyDictionary<string, PlayerRef> playerMap = new Dictionary<string, PlayerRef>();

        /// <summary>Gets whether the loaded map is young enough to serve.</summary>
        private bool IsCurrent => LastRefreshedUtc is { } refreshed && IsWithinMaxAge(refreshed);

        /// <inheritdoc />
        public async Task EnsureCurrentAsync(CancellationToken cancellationToken)
        {
            if (IsCurrent)
            {
                return;
            }

            // one fetch per instance however many requests arrive together
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (IsCurrent)
                {
                    return;
                }

                var stored = await store.ReadAsync(cancellationToken);
                if (stored is not null && IsWithinMaxAge(stored.RefreshedUtc))
                {
                    playerMap = stored.Players;
                    LastRefreshedUtc = stored.RefreshedUtc;

                    logger.LogInformation("Adopted the stored player map. Refreshed: {RefreshedUtc}, startable: {StartableCount}.",
                        stored.RefreshedUtc, playerMap.Count);

                    return;
                }

                await RefreshCoreAsync(cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Decides whether a timestamp is inside the configured maximum age.
        /// </summary>
        /// <param name="refreshedUtc">When the map was fetched.</param>
        /// <returns><c>true</c> when the map may still be served.</returns>
        private bool IsWithinMaxAge(DateTimeOffset refreshedUtc) =>
            DateTimeOffset.UtcNow - refreshedUtc < options.Value.MaxAge;

        /// <summary>
        /// Refreshes the PlayerMap by fetching the latest player data from the Sleeper API.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>A task that completes when the map has been replaced.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the player data cannot be retrieved.</exception>
        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                await RefreshCoreAsync(cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Fetches the dump, projects it and stores the result. Callers hold the gate.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>A task that completes when the map has been replaced.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the player data cannot be retrieved.</exception>
        private async Task RefreshCoreAsync(CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            logger.LogInformation("Refreshing player map from Sleeper API.");

            var players = await client.GetPlayersAsync(cancellationToken);

            if (players == null)
            {
                logger.LogError("Failed to retrieve players from Sleeper API. Duration: {DurationMs}ms", stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException("Failed to retrieve players from Sleeper API.");
            }

            // assign last: a failed refresh keeps yesterday's map
            playerMap = SleeperMapper.ToPlayerMap(players);
            LastRefreshedUtc = DateTimeOffset.UtcNow;

            // count is the tripwire: a dump that quietly shrinks shows up here
            logger.LogInformation(
                "Refreshed player map. Fetched: {FetchedCount}, startable: {StartableCount}, duration: {DurationMs}ms",
                players.Count, playerMap.Count, stopwatch.ElapsedMilliseconds);

            await store.WriteAsync(new StoredPlayerMap(LastRefreshedUtc.Value, playerMap), cancellationToken);
        }

        /// <summary>
        /// Gets the current mapping of player IDs to PlayerRef objects. This map is refreshed periodically by the RefreshAsync method.
        /// </summary>
        public IReadOnlyDictionary<string, PlayerRef> PlayerMap => playerMap;

        /// <summary>
        /// Gets when the map was last replaced, or null if it has never been refreshed.
        /// </summary>
        /// <remarks>Null and an empty map are different things; A8 reports every player as unresolved in both.</remarks>
        public DateTimeOffset? LastRefreshedUtc { get; private set; }
    }
}
