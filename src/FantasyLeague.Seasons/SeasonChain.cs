using System.ComponentModel;
using FantasyLeague.Sleeper;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// Every season the configured league can reach, by year.
    /// </summary>
    /// <remarks>The public seam. The implementation stays internal.</remarks>
    public interface ISeasonChain
    {
        /// <summary>
        /// Gets each reachable season's league id, the configured league's own season included.
        /// </summary>
        /// <returns>League ids by season. League ids are secrets (A2): never log them.</returns>
        Task<IReadOnlyDictionary<int, string>> GetAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Walks <c>previous_league_id</c> back from the configured league (D8), once a day.
    /// </summary>
    /// <remarks>
    /// Kept in memory rather than a table (A34): one Sleeper call per season of
    /// history per day per instance, and past league ids never reach the database.
    /// </remarks>
    /// <param name="sleeper">Reads each league's link.</param>
    /// <param name="cache">Holds the walk for a day.</param>
    /// <param name="options">The configured league.</param>
    internal sealed class SeasonChain(
        ISleeperReader sleeper,
        HybridCache cache,
        IOptions<LeagueOptions> options,
        ILogger<SeasonChain> logger) : ISeasonChain
    {
        /// <summary>A guard against a malformed chain; no league has fifty seasons of history.</summary>
        private const int MaxSeasons = 50;

        /// <summary>A new link appears once a year, at the rollover.</summary>
        private static readonly HybridCacheEntryOptions Daily = new() { Expiration = TimeSpan.FromHours(24) };

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<int, string>> GetAsync(CancellationToken cancellationToken)
        {
            var leagueId = options.Value.CurrentLeagueId;
            var walked = await cache.GetOrCreateAsync(
                $"chain:{leagueId}",
                async token => new Walked(await WalkAsync(leagueId, token)),
                Daily,
                cancellationToken: cancellationToken);

            return walked.Seasons;
        }

        /// <summary>
        /// Follows each league to the one it rolled over from until the chain ends.
        /// </summary>
        /// <param name="leagueId">The configured league.</param>
        /// <returns>League ids by season.</returns>
        private async Task<IReadOnlyDictionary<int, string>> WalkAsync(string leagueId, CancellationToken cancellationToken)
        {
            var seasons = new Dictionary<int, string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var next = leagueId;

            while (next is not null && seasons.Count < MaxSeasons && visited.Add(next))
            {
                var link = await sleeper.GetSeasonLinkAsync(next, cancellationToken);
                if (!seasons.TryAdd(link.Season, next))
                {
                    // two leagues claiming one season: keep the newer, stop rather than guess
                    logger.LogWarning("The season chain reached season {Season} twice; it stops there.", link.Season);
                    break;
                }

                next = link.PreviousLeagueId;
            }

            return seasons;
        }

        /// <summary>
        /// A walked chain, marked immutable so the cache hands back the same instance.
        /// </summary>
        /// <param name="Seasons">League ids by season.</param>
        [ImmutableObject(true)]
        private sealed record Walked(IReadOnlyDictionary<int, string> Seasons);
    }
}
