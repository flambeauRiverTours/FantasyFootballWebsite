using System.Text.Json;
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
        /// Keeps the fields of the NFL state anything downstream reads.
        /// </summary>
        /// <param name="state">The state as Sleeper returned it.</param>
        /// <returns>The current season, week and season type.</returns>
        internal static NFLState ToNFLState(NFLStateDto state) => new(state.Season, state.Week, state.SeasonType);

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
                    roster.OwnerId,
                    ToRecord(roster),
                    roster.Settings?.Division));
        }

        /// <summary>
        /// Sleeper's own record for a roster, kept to check ours against (T68).
        /// </summary>
        /// <param name="roster">The roster as Sleeper returned it.</param>
        /// <returns>The record, or null when Sleeper sent none.</returns>
        /// <remarks>
        /// Whole points and hundredths arrive in separate fields: <c>fpts</c> and
        /// <c>fpts_decimal</c>.
        /// </remarks>
        internal static TeamRecord? ToRecord(RosterDto roster) =>
            roster.Settings is { Wins: { } wins, Losses: { } losses } s
                ? new TeamRecord(
                    wins,
                    losses,
                    s.Ties ?? 0,
                    (s.Fpts ?? 0) + ((s.FptsDecimal ?? 0) / 100m),
                    (s.FptsAgainst ?? 0) + ((s.FptsAgainstDecimal ?? 0) / 100m))
                : null;

        /// <summary>
        /// Maps the winners bracket (T69), reading each side tolerantly (A46).
        /// </summary>
        /// <param name="matches">Sleeper's matches; null when it sent none.</param>
        /// <returns>The bracket; empty for none.</returns>
        /// <exception cref="InvalidDataException">A match or a side could not be read.</exception>
        internal static IReadOnlyList<PlayoffMatch> ToBracket(IReadOnlyList<BracketMatchDto?>? matches) =>
            matches is null
                ? []
                : [.. matches.Select(m => m is null
                    ? throw new InvalidDataException("Sleeper sent a null bracket match.")
                    : new PlayoffMatch(m.R, m.M, ToSide(m.T1, m.T1From), ToSide(m.T2, m.T2From), m.W, m.P))];

        /// <summary>
        /// Reads one side: a roster id, a reference standing in for one, or nothing yet.
        /// </summary>
        /// <param name="team">Sleeper's <c>t1</c> or <c>t2</c>.</param>
        /// <param name="from">Sleeper's <c>t1_from</c> or <c>t2_from</c>.</param>
        /// <returns>The side.</returns>
        private static BracketSide ToSide(JsonElement? team, JsonElement? from)
        {
            // a reference in place of the roster says where the side comes from; else its own from does
            var (source, match) =
                team is { ValueKind: JsonValueKind.Object } stand ? ToReference(stand)
                : from is { ValueKind: JsonValueKind.Object } reference ? ToReference(reference)
                : (BracketSource.None, 0);
            return team switch
            {
                { ValueKind: JsonValueKind.Number } id => new BracketSide(id.GetInt32(), source, match),
                null or { ValueKind: JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined } => new BracketSide(null, source, match),
                _ => throw new InvalidDataException("Sleeper sent a bracket side that is neither a roster nor a reference."),
            };
        }

        /// <summary>
        /// Reads <c>{"w": m}</c> or <c>{"l": m}</c>.
        /// </summary>
        /// <param name="reference">The object.</param>
        /// <returns>The source and its match.</returns>
        private static (BracketSource Source, int Match) ToReference(JsonElement reference) =>
            reference.TryGetProperty("w", out var w) && w.ValueKind == JsonValueKind.Number ? (BracketSource.WinnerOf, w.GetInt32())
            : reference.TryGetProperty("l", out var l) && l.ValueKind == JsonValueKind.Number ? (BracketSource.LoserOf, l.GetInt32())
            : throw new InvalidDataException("Sleeper sent a bracket reference that is neither a winner nor a loser.");

        /// <summary>
        /// Whether the league plays a median game: each week, a second result against the league's median score (Q12).
        /// </summary>
        /// <param name="league">The league as Sleeper returned it.</param>
        /// <returns><c>true</c> when <c>league_average_match</c> is set.</returns>
        internal static bool HasMedianGame(LeagueDto league) =>
            league.Settings?.LeagueAverageMatch > 0;

        /// <summary>
        /// The league's divisions (T72b): one per <c>settings.divisions</c>, each named from
        /// <c>metadata.division_N</c>, or "Division N" when that is blank, absent or not a string.
        /// </summary>
        /// <param name="league">The league as Sleeper returned it.</param>
        /// <returns>The divisions in number order, or null for fewer than two.</returns>
        internal static IReadOnlyList<Division>? ToDivisions(LeagueDto league) =>
            league.Settings?.Divisions is >= 2 and var count
                ? [.. Enumerable.Range(1, count).Select(n => new Division(n, DivisionName(league.Metadata, n)))]
                : null;

        /// <summary>
        /// One division's name: the commissioner's, trimmed, or "Division N".
        /// </summary>
        /// <param name="metadata">The league's metadata as sent, or null.</param>
        /// <param name="number">The division's number.</param>
        /// <returns>A name that is never blank.</returns>
        private static string DivisionName(JsonElement? metadata, int number) =>
            metadata is { ValueKind: JsonValueKind.Object } fields
                && fields.TryGetProperty($"division_{number}", out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString()?.Trim() is { Length: > 0 } name
                ? name
                : $"Division {number}";

        /// <summary>
        /// The league's display name, or a default when the commissioner set none.
        /// </summary>
        /// <param name="league">The league as Sleeper returned it.</param>
        /// <returns>A name that is never blank.</returns>
        internal static string ToLeagueName(LeagueDto league) =>
            string.IsNullOrWhiteSpace(league.Name) ? DefaultLeagueName : league.Name;

        /// <summary>
        /// The league's place in the season chain.
        /// </summary>
        /// <param name="league">The league as Sleeper returned it.</param>
        /// <returns>Its season and predecessor; an empty or <c>"0"</c> predecessor means none.</returns>
        internal static SeasonLink ToSeasonLink(LeagueDto league) =>
            new(league.Season, string.IsNullOrWhiteSpace(league.PreviousLeagueId) || league.PreviousLeagueId == "0"
                ? null
                : league.PreviousLeagueId);

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
        /// <param name="user">The manager, whose metadata may hold a team name.</param>
        /// <param name="rosterId">Their roster, used for the last-resort name.</param>
        /// <returns>A non-empty display name.</returns>
        /// <remarks>
        /// An unset team name arrives as an absent key, a null <c>metadata</c>, or
        /// an empty string (A6). All three fall back alike.
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
                unresolved,
                matchup.MatchupId);
        }
    }
}
