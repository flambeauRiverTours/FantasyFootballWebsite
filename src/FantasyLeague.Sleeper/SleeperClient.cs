using System.Net.Http.Json;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// A service for interacting with the Sleeper API.
    /// </summary>
    /// <param name="httpClient"></param>
    internal sealed class SleeperClient(
     HttpClient httpClient)
    {
        /// <summary>
        /// Gets the current state of the NFL season, including the current season, week, and season type.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The current state of the NFL season.</returns>
        internal Task<NflStateDto?> GetStateAsync(CancellationToken cancellationToken = default) => httpClient.GetFromJsonAsync<NflStateDto>("state/nfl", SleeperJson.Options, cancellationToken);

        /// <summary>
        /// Gets the league information for the specified league ID.
        /// </summary>
        /// <param name="leagueId">The league ID.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The league information.</returns>
        internal Task<LeagueDto?> GetLeagueAsync(string leagueId, CancellationToken cancellationToken = default) => httpClient.GetFromJsonAsync<LeagueDto>($"league/{leagueId}", SleeperJson.Options, cancellationToken);

        /// <summary>
        /// Gets the users in the specified league.
        /// </summary>
        /// <param name="leagueId">The league ID.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The users in the specified league.</returns>
        internal Task<IReadOnlyList<LeagueUserDto?>?> GetUsersAsync(string leagueId, CancellationToken cancellationToken = default) => httpClient.GetFromJsonAsync<IReadOnlyList<LeagueUserDto?>>($"league/{leagueId}/users", SleeperJson.Options, cancellationToken);

        /// <summary>
        /// Gets the rosters in the specified league.
        /// </summary>
        /// <param name="leagueId">The league ID.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The rosters in the specified league.</returns>
        internal Task<IReadOnlyList<RosterDto?>?> GetRostersAsync(string leagueId, CancellationToken cancellationToken = default) => httpClient.GetFromJsonAsync<IReadOnlyList<RosterDto?>>($"league/{leagueId}/rosters", SleeperJson.Options, cancellationToken);

        /// <summary>
        /// Gets the matchups for the specified league and week.
        /// </summary>
        /// <param name="leagueId">The league ID.</param>
        /// <param name="week">The week.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The matchups for the specified league and week.</returns>
        internal Task<IReadOnlyList<MatchupDto?>?> GetMatchupsAsync(string leagueId, int week, CancellationToken cancellationToken = default) => httpClient.GetFromJsonAsync<IReadOnlyList<MatchupDto?>>($"league/{leagueId}/matchups/{week}", SleeperJson.Options, cancellationToken);

        /// <summary>
        /// Gets the players in the NFL.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The players in the NFL.</returns>
        internal async Task<IReadOnlyDictionary<string, SleeperPlayerDto>?> GetPlayersAsync(CancellationToken cancellationToken = default) => await httpClient.GetFromJsonAsync<Dictionary<string, SleeperPlayerDto>>("players/nfl", SleeperJson.Options, cancellationToken);
    }
}
