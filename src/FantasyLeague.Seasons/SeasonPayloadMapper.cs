using FantasyLeague.Core;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// Turns an assembled season into the §09 response body.
    /// </summary>
    /// <remarks>
    /// A pure function of its arguments, so it can be tested without a Functions
    /// host. The endpoint supplies the clock rather than this reaching for it.
    /// </remarks>
    public static class SeasonPayloadMapper
    {
        /// <summary>
        /// Maps a snapshot to the season response.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <param name="asOfUtc">When the snapshot was assembled.</param>
        /// <returns>The response body.</returns>
        /// <remarks>
        /// Only regular-season weeks reach the payload (Q4, A37), less any
        /// unplayed weeks at its end, so the table, sigma, the chart and the radar
        /// all stop at the same week. Only counted teams are mapped, so the
        /// placeholder's record never reaches the page (A1).
        /// </remarks>
        public static SeasonPayload ToSeasonPayload(SeasonSnapshot snapshot, DateTimeOffset asOfUtc)
        {
            var lastWeek = snapshot.LastCountedWeek;
            var headToHead = snapshot.Format == LeagueFormat.HeadToHead;

            // shown only in head-to-head; the record check builds them in either format
            var records = headToHead ? snapshot.Records : null;
            var teams = snapshot.Teams
                .Select(team => ToTeam(team with { Weeks = [.. team.Weeks.Where(w => w.Week <= lastWeek)] }, records, snapshot.HasMedianGame))
                .OrderByDescending(t => t.Totals.Actual)
                .ToList();

            var leagueSlotAverages = SlotAverager.LeagueAverages(teams.Select(t => t.SlotAverages));
            return new SeasonPayload(
                snapshot.LeagueName,
                snapshot.Season,
                snapshot.IsCurrent,
                snapshot.CurrentWeek,
                Math.Min(snapshot.FinalThroughWeek, lastWeek),
                asOfUtc,
                SlotOrder(snapshot.Slots),
                leagueSlotAverages,
                teams,
                lastWeek,
                snapshot.Format,
                headToHead ? ToMatchups(snapshot) : null,
                Champions(snapshot, teams, records),
                snapshot.SeasonEndWeek,
                snapshot.IsComplete,
                ToPlayoffs(snapshot));
        }

        /// <summary>
        /// Maps the bracket for the page (T69, A46).
        /// </summary>
        /// <param name="snapshot">Its bracket, its rounds in weeks and every roster's weeks.</param>
        /// <returns>The bracket, or null when it cannot be shown.</returns>
        private static PlayoffsPayload? ToPlayoffs(SeasonSnapshot snapshot)
        {
            if (snapshot.PlayoffRounds is not { } rounds || snapshot.Bracket is not { } bracket)
            {
                return null;
            }

            var rosters = snapshot.Teams.Concat(snapshot.ExcludedTeams).ToList();
            var sides = new SideSource(rosters.ToDictionary(t => t.RosterId, t => t.Name), rosters.SelectMany(t => t.Weeks).ToLookup(w => w.RosterId));
            var finalRound = snapshot.Final?.Round;

            return new PlayoffsPayload([.. bracket.GroupBy(m => m.Round).OrderBy(g => g.Key).Select(round =>
                ToRound(round.Key, [.. round], rounds[round.Key], finalRound, snapshot.SettledThroughWeek, sides))]);
        }

        /// <summary>
        /// Maps one round: its name, then its main matches before its placement matches.
        /// </summary>
        /// <param name="round">1 for the first.</param>
        /// <param name="matches">Its matches, in any order.</param>
        /// <param name="played">Its weeks.</param>
        /// <param name="finalRound">The round with the final, which names the rounds before it.</param>
        /// <param name="settledThrough">The last week whose results stand.</param>
        /// <param name="sides">Every roster's name and weeks.</param>
        /// <returns>The round.</returns>
        private static RoundPayload ToRound(int round, IReadOnlyList<PlayoffMatch> matches, IReadOnlyList<int> played, int? finalRound, int settledThrough, SideSource sides)
        {
            var name = round == finalRound ? "Final" : round == finalRound - 1 ? "Semifinals" : $"Round {round}";
            var ordered = matches.OrderBy(m => m.Placement is null ? 0 : 1).ThenBy(m => m.Placement).ThenBy(m => m.Match);
            return new RoundPayload(round, played, name, [.. ordered.Select((match, i) =>
            {
                var winner = Playoffs.IsSettled(match, played, settledThrough) ? match.Winner : null;
                return new BracketMatchPayload(
                    match.Match,
                    Label(match.Placement, name, round == finalRound - 1, i + 1),
                    match.Placement,
                    winner is not null,
                    [sides.ToSide(match.First, winner, played), sides.ToSide(match.Second, winner, played)]);
            })]);
        }

        /// <summary>
        /// Names a match for its card.
        /// </summary>
        /// <param name="placement">The place it decides, or null.</param>
        /// <param name="round">Its round's name.</param>
        /// <param name="semifinal">Whether its round is the semifinals.</param>
        /// <param name="number">Its place in the round, from 1; placement matches come after the main ones.</param>
        /// <returns>"Final", "3rd place", "Semifinal 1" or "Round 1, game 2".</returns>
        private static string Label(int? placement, string round, bool semifinal, int number) => placement switch
        {
            1 => "Final",
            3 => "3rd place",
            { } place => $"{place}th place",
            null when semifinal => $"Semifinal {number}",
            null => $"{round}, game {number}",
        };

        /// <summary>
        /// Every roster's name and weeks, for the bracket's sides.
        /// </summary>
        /// <param name="Names">Every roster's name, the placeholder's included (A1, Q9).</param>
        /// <param name="Weeks">Every roster's weeks, playoff weeks included.</param>
        private sealed record SideSource(IReadOnlyDictionary<int, string> Names, ILookup<int, TeamWeek> Weeks)
        {
            /// <summary>
            /// Maps one side.
            /// </summary>
            /// <param name="side">The side.</param>
            /// <param name="winner">The match's winner once settled, else null.</param>
            /// <param name="played">Its round's weeks, over which its points are summed.</param>
            /// <returns>The side.</returns>
            public BracketSidePayload ToSide(BracketSide side, int? winner, IReadOnlyList<int> played)
            {
                var rows = side.RosterId is { } id ? Weeks[id].Where(w => played.Contains(w.Week)).ToList() : [];
                return new BracketSidePayload(
                    side.RosterId,
                    side.RosterId is { } known ? Names.GetValueOrDefault(known) : null,
                    side.From switch
                    {
                        BracketSource.WinnerOf => new BracketFromPayload(side.FromMatch, null),
                        BracketSource.LoserOf => new BracketFromPayload(null, side.FromMatch),
                        _ => null,
                    },
                    rows.Count == 0 ? null : rows.Sum(w => w.ActualPts),
                    side.RosterId is not null && side.RosterId == winner);
            }
        }

        /// <summary>
        /// Crowns the table's leaders once the season is complete (T70, A45).
        /// </summary>
        /// <param name="snapshot">Its format, its progress and whether it has playoffs.</param>
        /// <param name="teams">The table's teams, their weeks trimmed to the counted ones, so a playoff week decides nothing.</param>
        /// <param name="records">Every roster's record in head-to-head, or null.</param>
        /// <returns>The champions' roster ids, or null: before the season is complete, with playoffs (T69's to crown), or with records unknown.</returns>
        private static IReadOnlyList<int>? Champions(SeasonSnapshot snapshot, IReadOnlyList<TeamPayload> teams, IReadOnlyDictionary<int, TeamRecord>? records)
        {
            if (!snapshot.IsComplete)
            {
                return null;
            }

            // with playoffs the bracket's final decides, and its winner may have no table row (Q9)
            if (snapshot.HasBracket)
            {
                return snapshot.Final?.Winner is { } winner ? [winner] : null;
            }

            var leaders = snapshot.Format switch
            {
                LeagueFormat.TotalPoints => Standings.PointsLeaders(teams, t => t.Totals.Actual),
                _ when records is null => Array.Empty<TeamPayload>(),
                _ => Standings.RecordLeaders(teams, t => records.GetValueOrDefault(t.RosterId) ?? new TeamRecord(0, 0, 0, 0m, 0m)),
            };
            return leaders.Count == 0 ? null : [.. leaders.Select(t => t.RosterId).Order()];
        }

        /// <summary>
        /// Maps the seasons that can be served.
        /// </summary>
        /// <param name="current">The live season.</param>
        /// <param name="available">Every season that can be served, newest first, the current one included.</param>
        /// <returns>The response body.</returns>
        public static SeasonsPayload ToSeasonsPayload(int current, IReadOnlyList<int> available) =>
            new(current, available);

        /// <summary>
        /// Reduces the startable slots to the distinct codes, in roster_positions order.
        /// </summary>
        /// <param name="slots">Every startable slot, duplicates included.</param>
        /// <returns>The radar's axis order.</returns>
        /// <remarks>
        /// Eleven slots become eight axes for this league. R2: the silhouette
        /// depends on axis order, so this order is fixed and never re-sorted.
        /// </remarks>
        private static IReadOnlyList<string> SlotOrder(IReadOnlyList<Slot> slots) =>
            [.. slots.Select(s => s.Code).Distinct().Select(Slot.DisplayCode)];

        /// <summary>
        /// Maps one roster's season.
        /// </summary>
        /// <param name="team">The roster, its weeks already trimmed to the last counted week.</param>
        /// <param name="records">Every roster's record, or null when none is shown.</param>
        /// <param name="hasMedianGame">Whether the league plays a median game.</param>
        /// <returns>The team entry.</returns>
        private static TeamPayload ToTeam(TeamSeason team, IReadOnlyDictionary<int, TeamRecord>? records, bool hasMedianGame)
        {
            var spread = SpreadCalculator.Calculate(
                [.. team.Weeks.Where(w => w.IsFinal).Select(w => w.ActualPts)]);

            var totals = new TotalsPayload(
                team.Weeks.Sum(w => w.ActualPts),
                team.Weeks.Sum(w => w.IdealPts),
                spread?.Sigma,
                spread?.Cv,
                spread?.Mean,
                spread?.Min,
                spread?.Max);

            return new TeamPayload(
                team.RosterId,
                team.Name,
                totals,
                SlotAverager.SeasonAverages(team.Weeks)
                    .ToDictionary(kv => Slot.DisplayCode(kv.Key), kv => kv.Value),
                [.. team.Weeks
                    .OrderBy(w => w.Week)
                    .Select(w => new WeekPayload(w.Week, w.ActualPts, w.IdealPts, w.IsFinal))],
                records is null ? null : ToRecord(records.GetValueOrDefault(team.RosterId), hasMedianGame));
        }

        /// <summary>
        /// Maps the matchup week's games, the placeholder's included (T71).
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <returns>
        /// The games, or null when there are none to show: the preseason, a week stored before
        /// matchups were read, or a week Sleeper has not scheduled, which is stored as all byes (A41).
        /// </returns>
        private static MatchupsPayload? ToMatchups(SeasonSnapshot snapshot)
        {
            var week = snapshot.MatchupWeek;
            var rosters = snapshot.Teams.Concat(snapshot.ExcludedTeams).ToList();
            var names = rosters.ToDictionary(t => t.RosterId, t => t.Name);
            var weeks = rosters.SelectMany(t => t.Weeks).Where(w => w.Week == week).ToList();
            var games = HeadToHead.Games(weeks);
            MatchupTeamPayload Side(TeamWeek w) => new(w.RosterId, names[w.RosterId], w.ActualPts);

            // Games is null when any id is null, so every paired side has one
            return games is null or []
                ? null
                : new MatchupsPayload(
                    week,
                    week <= snapshot.FinalThroughWeek,
                    [.. games.Select(g => new GamePayload(g.First.MatchupId!.Value, [Side(g.First), Side(g.Second)]))],
                    snapshot.HasMedianGame ? HeadToHead.Median(weeks.Select(w => w.ActualPts)) : null);
        }

        /// <summary>
        /// Maps a roster's record for the page.
        /// </summary>
        /// <param name="record">The record, or null for a roster with no counted week yet.</param>
        /// <param name="hasMedianGame">Whether the league plays a median game, so an empty record carries an empty share too.</param>
        /// <returns>The record, no wins and no losses before a week settles.</returns>
        private static RecordPayload ToRecord(TeamRecord? record, bool hasMedianGame) =>
            record is null
                ? new RecordPayload(0, 0, 0, 0m, 0m, hasMedianGame ? new MedianPayload(0, 0, 0) : null)
                : new RecordPayload(
                    record.Wins,
                    record.Losses,
                    record.Ties,
                    record.PointsFor,
                    record.PointsAgainst,
                    record.Median is { } m ? new MedianPayload(m.Wins, m.Losses, m.Ties) : null);
    }
}
