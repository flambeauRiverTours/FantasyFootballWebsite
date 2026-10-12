using FantasyLeague.Core;
using FantasyLeague.Sleeper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// Holds the projected player map and replaces it on demand.
    /// </summary>
    /// <remarks>The public seam. The implementation and its dependencies stay internal.</remarks>
    public interface IPlayerMapRefresher
    {
        /// <summary>
        /// Gets the loaded map of player id to player.
        /// </summary>
        IReadOnlyDictionary<string, PlayerRef> PlayerMap { get; }

        /// <summary>
        /// Gets when the map was last replaced, or null if it has never been loaded.
        /// </summary>
        DateTimeOffset? LastRefreshedUtc { get; }

        /// <summary>
        /// Fetches a fresh map from Sleeper and stores it, whatever the age of the current one.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>A task that completes when the map has been replaced.</returns>
        Task RefreshAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Makes sure a map no older than the configured maximum age is loaded,
        /// reading the store before falling back to Sleeper.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>A task that completes when a usable map is in memory.</returns>
        Task EnsureCurrentAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Keeps one map per instance, adopted from the store when it is young enough (A12).
    /// </summary>
    /// <param name="sleeper">Where a fresh map comes from.</param>
    /// <param name="store">Where the map is kept between instances.</param>
    /// <param name="options">The maximum age.</param>
    /// <param name="time">Clock for the map's age.</param>
    /// <param name="logger">Logger.</param>
    internal sealed class PlayerMapRefresher(
        ISleeperReader sleeper,
        IPlayerMapStore store,
        IOptions<PlayerMapOptions> options,
        TimeProvider time,
        ILogger<PlayerMapRefresher> logger) : IPlayerMapRefresher
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        private volatile IReadOnlyDictionary<string, PlayerRef> playerMap = new Dictionary<string, PlayerRef>();

        /// <inheritdoc />
        public IReadOnlyDictionary<string, PlayerRef> PlayerMap => playerMap;

        /// <inheritdoc />
        /// <remarks>Null and an empty map are different things; A8 reports every player as unresolved in both.</remarks>
        public DateTimeOffset? LastRefreshedUtc { get; private set; }

        /// <summary>Gets whether the loaded map is young enough to serve.</summary>
        private bool IsCurrent => LastRefreshedUtc is { } refreshed && IsWithinMaxAge(refreshed);

        /// <inheritdoc />
        /// <exception cref="InvalidDataException">Sleeper answered with no players at all.</exception>
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
            time.GetUtcNow() - refreshedUtc < options.Value.MaxAge;

        /// <summary>
        /// Fetches the dump, projects it and stores the result. Callers hold the gate.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>A task that completes when the map has been replaced.</returns>
        /// <exception cref="InvalidDataException">Sleeper answered with no players at all.</exception>
        private async Task RefreshCoreAsync(CancellationToken cancellationToken)
        {
            var started = time.GetTimestamp();
            logger.LogInformation("Refreshing player map from Sleeper.");

            var dump = await sleeper.GetPlayersAsync(cancellationToken);

            // assign last: a failed refresh keeps yesterday's map
            playerMap = dump.Players;
            LastRefreshedUtc = time.GetUtcNow();

            // count is the tripwire: a dump that quietly shrinks shows up here
            logger.LogInformation(
                "Refreshed player map. Fetched: {FetchedCount}, startable: {StartableCount}, duration: {DurationMs}ms",
                dump.FetchedCount, playerMap.Count, (long)time.GetElapsedTime(started).TotalMilliseconds);

            await store.WriteAsync(new StoredPlayerMap(LastRefreshedUtc.Value, playerMap), cancellationToken);
        }
    }
}
