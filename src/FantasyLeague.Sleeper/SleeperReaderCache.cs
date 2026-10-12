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
        public ValueTask<IReadOnlyList<MatchupDto>> GetMatchupsAsync(string leagueId, int week, CancellationToken ct) =>
         cache.GetOrCreateAsync(
             $"matchups:{leagueId}:{week}",
             async ct2 => RequiredList(await client.GetMatchupsAsync(leagueId, week, ct2), "matchups"),
             Matchups,
             cancellationToken: ct);

        /// <summary>
        /// Gets the rosters for a given league and week, using caching to reduce API calls.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="ct">CancellationToken</param>
        /// <returns>Result of the API call</returns>
        public ValueTask<IReadOnlyList<RosterDto>> GetRostersAsync(string leagueId, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            $"rosters:{leagueId}",
            async ct2 => RequiredList(await client.GetRostersAsync(leagueId, ct2), "rosters"),
            Roster,
            cancellationToken: ct);

        /// <summary>
        /// Gets the league information for a given league and week, using caching to reduce API calls.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="ct">CancellationToken</param>
        /// <returns>Result of the API call</returns>
        public ValueTask<LeagueDto> GetLeagueAsync(string leagueId, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            $"league:{leagueId}",
            async ct2 => Required(await client.GetLeagueAsync(leagueId, ct2), "league"),
            League,
            cancellationToken: ct);

        /// <summary>
        /// Gets the NFL state information, using caching to reduce API calls.
        /// </summary>
        /// <param name="ct">CancellationToken</param>
        /// <returns>Result of the API call</returns>
        public ValueTask<NflStateDto> GetStateAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(
            "nfl_state",
            async ct2 => Required(await client.GetStateAsync(ct2), "NFL state"),
            NFLState,
            cancellationToken: ct);

        /// <summary>
        /// Gets the users for a given league and week, using caching to reduce API calls.
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="ct">CancellationToken</param>
        /// <returns>Result of the API call </returns>
        public ValueTask<IReadOnlyList<LeagueUserDto>> GetUsersAsync(string leagueId, CancellationToken ct) =>
        cache.GetOrCreateAsync(
            $"users:{leagueId}",
            async ct2 => RequiredList(await client.GetUsersAsync(leagueId, ct2), "users"),
            Users,
            cancellationToken: ct);

        /// <summary>
        /// Rejects a response Sleeper sent as <c>null</c>.
        /// </summary>
        /// <param name="value">The deserialized response.</param>
        /// <param name="what">What was requested, for the error message.</param>
        /// <returns>The response, known to be present.</returns>
        /// <exception cref="InvalidDataException">Sleeper returned nothing.</exception>
        private static T Required<T>(T? value, string what) where T : class =>
            value ?? throw new InvalidDataException($"Sleeper returned no {what}.");

        /// <summary>
        /// Rejects a list Sleeper sent as <c>null</c> or with a <c>null</c> entry.
        /// </summary>
        /// <param name="values">The deserialized response.</param>
        /// <param name="what">What was requested, for the error message.</param>
        /// <returns>The list, known to be present and free of nulls.</returns>
        /// <exception cref="InvalidDataException">Sleeper returned nothing, or a null entry.</exception>
        private static IReadOnlyList<T> RequiredList<T>(IReadOnlyList<T?>? values, string what) where T : class
        {
            var list = Required(values, what);
            return list.Any(v => v is null)
                ? throw new InvalidDataException($"Sleeper returned a null entry in {what}.")
                : [.. list.OfType<T>()];
        }

    }
}
