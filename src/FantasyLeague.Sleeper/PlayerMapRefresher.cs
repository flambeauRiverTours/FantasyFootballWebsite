using System.Diagnostics;
using Microsoft.Extensions.Logging;

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
    }

    internal sealed class PlayerMapRefresher(SleeperClient client, ILogger<PlayerMapRefresher> logger) : IPlayerMapRefresher
    {
        private volatile IReadOnlyDictionary<string, PlayerRef> playerMap = new Dictionary<string, PlayerRef>();

        /// <summary>
        /// Refreshes the PlayerMap by fetching the latest player data from the Sleeper API.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken</param>
        /// <returns>A task that completes when the map has been replaced.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the player data cannot be retrieved.</exception>
        public async Task RefreshAsync(CancellationToken cancellationToken)
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
