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
        /// <returns>The map of player id to player.</returns>
        /// <exception cref="InvalidDataException">No usable map was stored and Sleeper answered with no players.</exception>
        Task<IReadOnlyDictionary<string, PlayerRef>> EnsureCurrentAsync(CancellationToken cancellationToken);
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

        /// <summary>
        /// The map and when it was fetched, swapped as one so a reader never
        /// sees one without the other. Null until the first load.
        /// </summary>
        private volatile StoredPlayerMap? current;

        /// <summary>Gets the loaded map if it is young enough to serve.</summary>
        private StoredPlayerMap? Usable => current is { } map && IsWithinMaxAge(map.RefreshedUtc) ? map : null;

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
        public async Task<IReadOnlyDictionary<string, PlayerRef>> EnsureCurrentAsync(CancellationToken cancellationToken)
        {
            if (Usable is { } loaded)
            {
                return loaded.Players;
            }

            // one fetch per instance however many requests arrive together
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (Usable is { } raced)
                {
                    return raced.Players;
                }

                var stored = await store.ReadAsync(cancellationToken);
                if (stored is not null && IsWithinMaxAge(stored.RefreshedUtc))
                {
                    current = stored;

                    logger.LogInformation("Adopted the stored player map. Refreshed: {RefreshedUtc}, startable: {StartableCount}.",
                        stored.RefreshedUtc, stored.Players.Count);

                    return stored.Players;
                }

                return (await RefreshCoreAsync(cancellationToken)).Players;
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
        /// <returns>The new map.</returns>
        /// <exception cref="InvalidDataException">Sleeper answered with no players at all.</exception>
        private async Task<StoredPlayerMap> RefreshCoreAsync(CancellationToken cancellationToken)
        {
            var started = time.GetTimestamp();
            logger.LogInformation("Refreshing player map from Sleeper.");

            var players = await sleeper.GetPlayersAsync(cancellationToken);

            // assign last: a failed refresh keeps yesterday's map
            var refreshed = new StoredPlayerMap(time.GetUtcNow(), players);
            current = refreshed;

            // count is the tripwire: a dump that quietly shrinks shows up here
            logger.LogInformation(
                "Refreshed player map. Startable: {StartableCount}, duration: {DurationMs}ms",
                players.Count, (long)time.GetElapsedTime(started).TotalMilliseconds);

            await store.WriteAsync(refreshed, cancellationToken);
            return refreshed;
        }
    }
}
