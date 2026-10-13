using FantasyLeague.Core;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// The one place Sleeper's shapes become the domain's shapes.
    /// </summary>
    /// <remarks>
    /// Keeping this explicit and in a single class is the point of having wire
    /// types at all: a Sleeper schema change lands here, not in the solver, the
    /// tests and the API contract at once.
    /// </remarks>
    internal static class SleeperMapper
    {
        /// <summary>
        /// The league name used when the commissioner set none.
        /// </summary>
        private const string DefaultLeagueName = "Fantasy Football League";

        /// <summary>The string Sleeper puts in <c>starters</c> for a slot nobody filled.</summary>
        private const string EmptySlot = "0";

        /// <summary>
        /// Projects the raw player dump down to the fields anything downstream
        /// needs, dropping entries that cannot be started.
        /// </summary>
        /// <param name="dump">The parsed <c>/v1/players/nfl</c> response.</param>
        /// <returns>A map of player id to <see cref="PlayerRef"/>.</returns>
        /// <remarks>
        /// Entries with no fantasy positions are dropped — around 330 of roughly
        /// 12,200 — because nothing can be started at no position. The name falls
        /// back to the id for the roughly 30 entries, mostly defences, that carry
        /// no full name.
        /// </remarks>
        internal static IReadOnlyDictionary<string, PlayerRef> ToPlayerMap(
            IReadOnlyDictionary<string, SleeperPlayerDto> dump)
        {
            var map = new Dictionary<string, PlayerRef>(dump.Count, StringComparer.Ordinal);

            foreach (var (id, player) in dump)
            {
                var positions = player.FantasyPositions;
                if (positions is not { Count: > 0 })
                {
                    continue;
                }

                map[id] = new PlayerRef(player.FullName ?? id, player.Team, positions);
            }

            return map;
        }

        /// <summary>
        /// Keeps the two fields of the NFL state anything downstream reads.
        /// </summary>
        /// <param name="state">The state as Sleeper returned it.</param>
        /// <returns>The current season and week.</returns>
        internal static NFLState ToNFLState(NFLStateDto state) => new(state.Season, state.Week);

        /// <summary>
        /// Joins each roster to its owner, and so to the name it is shown under.
        /// </summary>
        /// <param name="users">The league's users.</param>
        /// <param name="rosters">The league's rosters.</param>
        /// <returns>Every roster by roster id.</returns>
        internal static IReadOnlyDictionary<int, SleeperTeam> ToTeams(
            IReadOnlyList<LeagueUserDto> users,
            IReadOnlyList<RosterDto> rosters)
        {
            var byUserId = users.ToDictionary(u => u.UserId, StringComparer.Ordinal);

            return rosters.ToDictionary(
                roster => roster.RosterId,
                roster => new SleeperTeam(
                    roster.OwnerId is not null && byUserId.TryGetValue(roster.OwnerId, out var user)
                        ? ResolveTeamName(user, roster.RosterId)
                        : SleeperTeam.FallbackName(roster.RosterId),
                    roster.OwnerId));
        }

        /// <summary>
        /// The league's display name, or a default when the commissioner set none.
        /// </summary>
        /// <param name="league">The league as Sleeper returned it.</param>
        /// <returns>A name that is never blank.</returns>
        internal static string ToLeagueName(LeagueDto league) =>
            string.IsNullOrWhiteSpace(league.Name) ? DefaultLeagueName : league.Name;

        /// <summary>
        /// The league's startable slots, in roster order.
        /// </summary>
        /// <param name="league">The league as Sleeper returned it.</param>
        /// <returns>Every startable slot.</returns>
        /// <exception cref="InvalidDataException">The league has no roster positions.</exception>
        internal static IReadOnlyList<Slot> ToSlots(LeagueDto league) =>
            Slot.Parse(league.RosterPositions
                ?? throw new InvalidDataException("Sleeper returned a league with no roster positions."));

        /// <summary>
        /// Picks a display name for one manager: their team name, else their
        /// handle, else the roster number.
        /// </summary>
        /// <param name="user">The manager.</param>
        /// <param name="rosterId">Their roster, used for the last-resort name.</param>
        /// <returns>A non-empty display name.</returns>
        /// <remarks>
        /// The design expects an unset team name to arrive as an empty string. It
        /// does not: for two of six managers in this league the <c>team_name</c>
        /// key is absent from <c>metadata</c> entirely, and <c>metadata</c> itself
        /// can be null. All three cases have to collapse to the same fallback,
        /// which is what <see cref="string.IsNullOrWhiteSpace"/> buys.
        /// </remarks>
        internal static string ResolveTeamName(LeagueUserDto user, int rosterId)
        {
            var teamName = user.Metadata?.TeamName;
            if (!string.IsNullOrWhiteSpace(teamName))
            {
                return teamName;
            }

            if (!string.IsNullOrWhiteSpace(user.DisplayName))
            {
                return user.DisplayName;
            }

            return SleeperTeam.FallbackName(rosterId);
        }

        /// <summary>
        /// Reads the lineup in slot order, keeping each empty slot in its place.
        /// </summary>
        /// <param name="matchup">The roster's entry for that week.</param>
        /// <returns>One entry per startable slot, null where Sleeper sent <c>"0"</c>.</returns>
        internal static IReadOnlyList<string?> ToSlotStarters(MatchupDto matchup) =>
            [.. (matchup.Starters ?? []).Select(id => id == EmptySlot ? null : id)];

        /// <summary>
        /// Turns one matchup entry into the shape the solver and the invariants
        /// operate on.
        /// </summary>
        /// <param name="matchup">The roster's entry for that week.</param>
        /// <param name="playerMap">The projected player map from <see cref="ToPlayerMap"/>.</param>
        /// <returns>The mapped week, including any ids the map could not explain.</returns>
        /// <remarks>
        /// Three decisions are made here rather than left to the caller.
        /// <list type="bullet">
        /// <item>
        /// A commissioner override wins. <c>custom_points</c> is null on an
        /// untouched matchup; when it is set it is the score Sleeper shows, so
        /// ignoring it would desync the standings from the app after a single
        /// stat correction, with no signal that it had happened.
        /// </item>
        /// <item>
        /// A player in <c>players</c> with no entry in <c>players_points</c>
        /// scored zero. That is data, not an error — a bye week produces it.
        /// </item>
        /// <item>
        /// A player the map cannot explain is reported, not dropped. See
        /// <see cref="RosterWeek.UnresolvedPlayerIds"/>.
        /// </item>
        /// </list>
        /// </remarks>
        internal static RosterWeek ToRosterWeek(
            MatchupDto matchup,
            IReadOnlyDictionary<string, PlayerRef> playerMap)
        {
            var scores = matchup.PlayersPoints;
            var rosteredIds = (matchup.Players ?? []).Where(id => id != EmptySlot);

            var players = new List<RosteredPlayer>();
            var unresolved = new List<string>();

            foreach (var id in rosteredIds)
            {
                if (!playerMap.TryGetValue(id, out var reference))
                {
                    unresolved.Add(id);
                    continue;
                }

                var points = scores is not null && scores.TryGetValue(id, out var scored) ? scored : 0m;
                players.Add(new RosteredPlayer(new PlayerId(id), reference.Name, reference.FantasyPositions, points));
            }

            return new RosterWeek(
                matchup.RosterId,
                matchup.CustomPoints ?? matchup.Points ?? 0m,
                matchup.Points ?? 0m,
                players,
                matchup.StartersPoints ?? [],
                ToSlotStarters(matchup),
                matchup.PlayersPoints ?? new Dictionary<string, decimal>(),
                unresolved);
        }
    }
}
