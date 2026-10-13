using System.ComponentModel;
using FantasyLeague.Core;
using Microsoft.Extensions.Caching.Hybrid;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Everything this application reads from Sleeper, already in the domain's shapes.
    /// </summary>
    /// <remarks>
    /// The project seam. Wire DTOs stay behind it, so a Sleeper schema change
    /// lands in this project and nowhere else.
    /// </remarks>
    public interface ISleeperReader
    {
        /// <summary>
        /// Gets the season and week Sleeper considers current.
        /// </summary>
        /// <returns>The current NFL season and week.</returns>
        Task<NFLState> GetNFLStateAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Gets a league's name, slots and teams.
        /// </summary>
        /// <returns>The league as this season's standings need it.</returns>
        Task<SleeperLeague> GetLeagueAsync(string leagueId, CancellationToken cancellationToken);

        /// <summary>
        /// Gets a league's season and the league it rolled over from, from the league object alone.
        /// </summary>
        /// <returns>One link in the season chain (D8).</returns>
        Task<SeasonLink> GetSeasonLinkAsync(string leagueId, CancellationToken cancellationToken);

        /// <summary>
        /// Gets every roster's week, joined to the player map.
        /// </summary>
        /// <param name="playerMap">The projected player map.</param>
        /// <returns>One entry per roster.</returns>
        Task<IReadOnlyList<RosterWeek>> GetWeekAsync(
            string leagueId,
            int week,
            IReadOnlyDictionary<string, PlayerRef> playerMap,
            CancellationToken cancellationToken);

        /// <summary>
        /// Fetches the 14 MB player dump and projects it to the slim map. Never cached:
        /// the daily refresh is the only caller.
        /// </summary>
        /// <returns>Every startable player by id.</returns>
        /// <exception cref="InvalidDataException">Sleeper answered with no players at all.</exception>
        Task<IReadOnlyDictionary<string, PlayerRef>> GetPlayersAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Reads through a short-lived cache per endpoint, then maps to the domain.
    /// </summary>
    /// <param name="client">The typed Sleeper client.</param>
    /// <param name="cache">The cache in front of it.</param>
    internal sealed class SleeperReader(SleeperClient client, HybridCache cache) : ISleeperReader
    {
        /// <summary>Scores move during games; 15 seconds is close enough to live.</summary>
        private static readonly HybridCacheEntryOptions Matchups = new() { Expiration = TimeSpan.FromSeconds(15) };

        /// <summary>
        /// Who owns each roster, for team names and A1's exclusion. That changes
        /// only when the commissioner reassigns a team, so this could be far
        /// longer; at 0.2 requests a minute it is not worth the wait.
        /// </summary>
        private static readonly HybridCacheEntryOptions Rosters = new() { Expiration = TimeSpan.FromMinutes(5) };

        /// <summary>The league's name and slots are set once a season.</summary>
        private static readonly HybridCacheEntryOptions League = new() { Expiration = TimeSpan.FromHours(24) };

        /// <summary>The week rolls over once a week; 5 minutes bounds how late the site notices.</summary>
        private static readonly HybridCacheEntryOptions NFLState = new() { Expiration = TimeSpan.FromMinutes(5) };

        /// <summary>
        /// Team names. A manager who renames their team waits up to an hour to
        /// see it; renames are rare, and an hour saves 0.18 requests a minute
        /// over five minutes.
        /// </summary>
        private static readonly HybridCacheEntryOptions Users = new() { Expiration = TimeSpan.FromHours(1) };

        /// <inheritdoc />
        public async Task<NFLState> GetNFLStateAsync(CancellationToken cancellationToken)
        {
            var state = await GetOrFetchAsync(
                "nfl_state",
                async token => Required(await client.GetNFLStateAsync(token), "NFL state"),
                NFLState,
                cancellationToken);

            return SleeperMapper.ToNFLState(state);
        }

        /// <inheritdoc />
        public async Task<SleeperLeague> GetLeagueAsync(string leagueId, CancellationToken cancellationToken)
        {
            var league = await GetLeagueDtoAsync(leagueId, cancellationToken);

            var users = await GetOrFetchAsync(
                $"users:{leagueId}",
                async token => RequiredList(await client.GetUsersAsync(leagueId, token), "users"),
                Users,
                cancellationToken);

            var rosters = await GetOrFetchAsync(
                $"rosters:{leagueId}",
                async token => RequiredList(await client.GetRostersAsync(leagueId, token), "rosters"),
                Rosters,
                cancellationToken);

            return new SleeperLeague(
                SleeperMapper.ToLeagueName(league),
                SleeperMapper.ToSlots(league),
                SleeperMapper.ToTeams(users, rosters));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RosterWeek>> GetWeekAsync(
            string leagueId,
            int week,
            IReadOnlyDictionary<string, PlayerRef> playerMap,
            CancellationToken cancellationToken)
        {
            var matchups = await GetOrFetchAsync(
                $"matchups:{leagueId}:{week}",
                async token => RequiredList(await client.GetMatchupsAsync(leagueId, week, token), "matchups"),
                Matchups,
                cancellationToken);

            return [.. matchups.Select(matchup => SleeperMapper.ToRosterWeek(matchup, playerMap))];
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<string, PlayerRef>> GetPlayersAsync(CancellationToken cancellationToken)
        {
            // a request that fails throws before this; null means Sleeper's body was literally null
            var dump = Required(await client.GetPlayersAsync(cancellationToken), "players");

            return SleeperMapper.ToPlayerMap(dump);
        }

        /// <inheritdoc />
        public async Task<SeasonLink> GetSeasonLinkAsync(string leagueId, CancellationToken cancellationToken) =>
            SleeperMapper.ToSeasonLink(await GetLeagueDtoAsync(leagueId, cancellationToken));

        /// <summary>
        /// Reads the league object through its 24-hour cache, shared by the standings and the chain walk.
        /// </summary>
        /// <returns>The league object.</returns>
        private Task<LeagueDto> GetLeagueDtoAsync(string leagueId, CancellationToken cancellationToken) =>
            GetOrFetchAsync(
                $"league:{leagueId}",
                async token => Required(await client.GetLeagueAsync(leagueId, token), "league"),
                League,
                cancellationToken);

        /// <summary>
        /// Reads through the cache, keeping the response as the one instance it was fetched as.
        /// </summary>
        /// <typeparam name="T">The response shape.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="fetch">Calls Sleeper on a miss.</param>
        /// <param name="options">How long the response lives.</param>
        /// <returns>The cached or fetched response.</returns>
        /// <remarks>
        /// HybridCache hands back a freshly deserialized copy of anything it
        /// cannot see is immutable, so a bare list would be copied on every hit.
        /// The wrapper says it is immutable; nothing here mutates a response.
        /// </remarks>
        private async Task<T> GetOrFetchAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> fetch,
            HybridCacheEntryOptions options,
            CancellationToken cancellationToken)
        {
            var entry = await cache.GetOrCreateAsync(
                key,
                async token => new Cached<T>(await fetch(token)),
                options,
                cancellationToken: cancellationToken);

            return entry.Value;
        }

        /// <summary>
        /// Refuses a response Sleeper sent as <c>null</c>.
        /// </summary>
        /// <typeparam name="T">The response shape.</typeparam>
        /// <param name="value">The response.</param>
        /// <param name="what">What was asked for, for the message.</param>
        /// <returns>The response, never null.</returns>
        private static T Required<T>(T? value, string what)
            where T : class =>
            value ?? throw new InvalidDataException($"Sleeper returned no {what}.");

        /// <summary>
        /// Refuses a list Sleeper sent as <c>null</c>, or with a <c>null</c> entry.
        /// </summary>
        /// <typeparam name="T">The entry shape.</typeparam>
        /// <param name="values">The response.</param>
        /// <param name="what">What was asked for, for the message.</param>
        /// <returns>The entries, none of them null.</returns>
        private static IReadOnlyList<T> RequiredList<T>(IReadOnlyList<T?>? values, string what)
            where T : class
        {
            var list = Required(values, what);

            return list.Any(v => v is null)
                ? throw new InvalidDataException($"Sleeper returned a null entry in {what}.")
                : [.. list.OfType<T>()];
        }
    }

    /// <summary>
    /// A cached response, marked immutable so HybridCache hands back the same instance.
    /// </summary>
    /// <typeparam name="T">The response shape.</typeparam>
    /// <param name="Value">The response.</param>
    [ImmutableObject(true)]
    internal sealed record Cached<T>(T Value);

}
