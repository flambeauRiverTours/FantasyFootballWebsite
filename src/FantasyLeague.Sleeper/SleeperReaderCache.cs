
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Hybrid;

namespace FantasyLeague.Sleeper
{
    internal sealed class SleeperReaderCache(SleeperClient client, HybridCache cache)
    {
        private static readonly HybridCacheEntryOptions Matchups = new() { Expiration = TimeSpan.FromSeconds(15) }; //cache matchups for 5 seconds - close enough to real time

        private static readonly HybridCacheEntryOptions Roster = new() { Expiration = TimeSpan.FromMinutes(5) }; //cache rosters for 5 minutes - should be few changes, but players may check website immediately after changing rroster

        private static readonly HybridCacheEntryOptions League = new() { Expiration = TimeSpan.FromHours(24) }; //cache league for 24 hours - should rarely change

        private static readonly HybridCacheEntryOptions NFLState = new() { Expiration = TimeSpan.FromMinutes(5) };

        private static readonly HybridCacheEntryOptions Users = new() { Expiration = TimeSpan.FromHours(1) };



        /// <summary>
        /// Gets the matchups for a given league and week, using caching to reduce API calls.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="week">Week</param>
        /// <param name="ct">CancellationToken</param>
        /// <returns>Result of the API call</returns>
        public ValueTask<IReadOnlyList<MatchupDto?>?> GetMatchupsAsync(string leagueId, int week, CancellationToken ct) =>
         cache.GetOrCreateAsync(
             $"matchups:{leagueId}:{week}",
             ct2 => new ValueTask<IReadOnlyList<MatchupDto?>?>(
                        client.GetMatchupsAsync(leagueId, week, ct2)),
             Matchups,
             cancellationToken: ct);

        /// <summary>
        /// Gets the rosters for a given league and week, using caching to reduce API calls.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="ct">CancellationToken</param>
        /// <returns>Result of the API call</returns>
        public ValueTask<IReadOnlyList<RosterDto?>?> GetRostersAsync(string leagueId, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            $"rosters:{leagueId}",
            ct2 => new ValueTask<IReadOnlyList<RosterDto?>?>(
                       client.GetRostersAsync(leagueId, ct2)),
            Roster,
            cancellationToken: ct);

        /// <summary>
        /// Gets the league information for a given league and week, using caching to reduce API calls.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="ct">CancellationToken</param>
        /// <returns>Result of the API call</returns>
        public ValueTask<LeagueDto?> GetLeagueAsync(string leagueId, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            $"league:{leagueId}",
            ct2 => new ValueTask<LeagueDto?>(
                       client.GetLeagueAsync(leagueId, ct2)),
            League,
            cancellationToken: ct);

        /// <summary>
        /// Gets the NFL state information, using caching to reduce API calls.
        /// </summary>
        /// <param name="ct">CancellationToken</param>
        /// <returns>Result of the API call</returns>
        public ValueTask<NflStateDto?> GetStateAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(
            "nfl_state",
            ct2 => new ValueTask<NflStateDto?>(
                       client.GetStateAsync(ct2)),
            NFLState,
            cancellationToken: ct);

        /// <summary>
        /// Gets the users for a given league and week, using caching to reduce API calls.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="ct">CancellationToken</param>
        /// <returns>Result of the API call </returns>
        public ValueTask<IReadOnlyList<LeagueUserDto?>?> GetUsersAsync(string leagueId, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            $"users:{leagueId}",
            ct2 => new ValueTask<IReadOnlyList<LeagueUserDto?>?>(
                       client.GetUsersAsync(leagueId, ct2)),
            Users,
            cancellationToken: ct);

    }
}
