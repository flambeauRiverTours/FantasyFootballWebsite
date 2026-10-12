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
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The current NFL season and week.</returns>
        Task<NFLState> GetNFLStateAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Gets a league's name, slots and teams.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The league as this season's standings need it.</returns>
        Task<SleeperLeague> GetLeagueAsync(string leagueId, CancellationToken cancellationToken);

        /// <summary>
        /// Gets every roster's week, joined to the player map.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="week">The week.</param>
        /// <param name="playerMap">The projected player map.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
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
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The projected map, with how many players the dump held.</returns>
        /// <exception cref="InvalidDataException">Sleeper answered with no players at all.</exception>
        Task<PlayerDump> GetPlayersAsync(CancellationToken cancellationToken);
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

        /// <summary>A manager may check the site straight after changing their roster.</summary>
        private static readonly HybridCacheEntryOptions Rosters = new() { Expiration = TimeSpan.FromMinutes(5) };

        /// <summary>The league's name and slots are set once a season.</summary>
        private static readonly HybridCacheEntryOptions League = new() { Expiration = TimeSpan.FromHours(24) };

        /// <summary>The week rolls over once a week; 5 minutes bounds how late the site notices.</summary>
        private static readonly HybridCacheEntryOptions NFLState = new() { Expiration = TimeSpan.FromMinutes(5) };

        /// <summary>Team names change rarely.</summary>
        private static readonly HybridCacheEntryOptions Users = new() { Expiration = TimeSpan.FromHours(1) };

        /// <inheritdoc />
        public async Task<NFLState> GetNFLStateAsync(CancellationToken cancellationToken)
        {
            var state = await cache.GetOrCreateAsync(
                "nfl_state",
                async token => Required(await client.GetNFLStateAsync(token), "NFL state"),
                NFLState,
                cancellationToken: cancellationToken);

            return SleeperMapper.ToNFLState(state);
        }

        /// <inheritdoc />
        public async Task<SleeperLeague> GetLeagueAsync(string leagueId, CancellationToken cancellationToken)
        {
            var league = await cache.GetOrCreateAsync(
                $"league:{leagueId}",
                async token => Required(await client.GetLeagueAsync(leagueId, token), "league"),
                League,
                cancellationToken: cancellationToken);

            var users = await cache.GetOrCreateAsync(
                $"users:{leagueId}",
                async token => RequiredList(await client.GetUsersAsync(leagueId, token), "users"),
                Users,
                cancellationToken: cancellationToken);

            var rosters = await cache.GetOrCreateAsync(
                $"rosters:{leagueId}",
                async token => RequiredList(await client.GetRostersAsync(leagueId, token), "rosters"),
                Rosters,
                cancellationToken: cancellationToken);

            return new SleeperLeague(
                SleeperMapper.ToLeagueName(league),
                SleeperMapper.ToSlots(league),
                SleeperMapper.ToTeamNames(users, rosters),
                SleeperMapper.ToRosterOwnerIds(rosters));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<RosterWeek>> GetWeekAsync(
            string leagueId,
            int week,
            IReadOnlyDictionary<string, PlayerRef> playerMap,
            CancellationToken cancellationToken)
        {
            var matchups = await cache.GetOrCreateAsync(
                $"matchups:{leagueId}:{week}",
                async token => RequiredList(await client.GetMatchupsAsync(leagueId, week, token), "matchups"),
                Matchups,
                cancellationToken: cancellationToken);

            return [.. matchups.Select(matchup => SleeperMapper.ToRosterWeek(matchup, playerMap))];
        }

        /// <inheritdoc />
        public async Task<PlayerDump> GetPlayersAsync(CancellationToken cancellationToken)
        {
            // a request that fails throws before this; null means Sleeper's body was literally null
            var dump = Required(await client.GetPlayersAsync(cancellationToken), "players");

            return SleeperMapper.ToPlayerDump(dump);
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
}
