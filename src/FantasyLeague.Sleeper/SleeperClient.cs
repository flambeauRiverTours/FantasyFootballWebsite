using System.Net.Http.Json;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// The six Sleeper endpoints this application reads, as raw DTOs.
    /// </summary>
    /// <remarks>
    /// Each method returns null when Sleeper's body is literally <c>null</c>;
    /// <see cref="SleeperReader"/> decides what that means.
    /// </remarks>
    /// <param name="httpClient">The typed client, with the rate limit and resilience pipeline attached.</param>
    internal sealed class SleeperClient(HttpClient httpClient)
    {
        /// <summary>
        /// Reads <c>/state/nfl</c>: the current season and week.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The state, or null.</returns>
        internal Task<NFLStateDto?> GetNFLStateAsync(CancellationToken cancellationToken = default) =>
            httpClient.GetFromJsonAsync<NFLStateDto>("state/nfl", SleeperJson.Options, cancellationToken);

        /// <summary>
        /// Reads <c>/league/{id}</c>: the league's name and slots.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The league, or null.</returns>
        internal Task<LeagueDto?> GetLeagueAsync(string leagueId, CancellationToken cancellationToken = default) =>
            httpClient.GetFromJsonAsync<LeagueDto>($"league/{leagueId}", SleeperJson.Options, cancellationToken);

        /// <summary>
        /// Reads <c>/league/{id}/users</c>: each manager's handle and team name.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The users, or null.</returns>
        internal Task<IReadOnlyList<LeagueUserDto?>?> GetUsersAsync(string leagueId, CancellationToken cancellationToken = default) =>
            httpClient.GetFromJsonAsync<IReadOnlyList<LeagueUserDto?>>($"league/{leagueId}/users", SleeperJson.Options, cancellationToken);

        /// <summary>
        /// Reads <c>/league/{id}/rosters</c>: which manager owns which roster.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The rosters, or null.</returns>
        internal Task<IReadOnlyList<RosterDto?>?> GetRostersAsync(string leagueId, CancellationToken cancellationToken = default) =>
            httpClient.GetFromJsonAsync<IReadOnlyList<RosterDto?>>($"league/{leagueId}/rosters", SleeperJson.Options, cancellationToken);

        /// <summary>
        /// Reads <c>/league/{id}/matchups/{week}</c>: every roster's points and lineup that week.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="week">The week.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The matchups, or null.</returns>
        internal Task<IReadOnlyList<MatchupDto?>?> GetMatchupsAsync(string leagueId, int week, CancellationToken cancellationToken = default) =>
            httpClient.GetFromJsonAsync<IReadOnlyList<MatchupDto?>>($"league/{leagueId}/matchups/{week}", SleeperJson.Options, cancellationToken);

        /// <summary>
        /// Reads <c>/players/nfl</c>: the 14 MB dump of every NFL player.
        /// </summary>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>Every player by id, or null.</returns>
        internal async Task<IReadOnlyDictionary<string, SleeperPlayerDto>?> GetPlayersAsync(CancellationToken cancellationToken = default) =>
            await httpClient.GetFromJsonAsync<Dictionary<string, SleeperPlayerDto>>("players/nfl", SleeperJson.Options, cancellationToken);
    }
}
