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
                .Select(team => ToTeam(team with { Weeks = [.. team.Weeks.Where(w => w.Week <= lastWeek)] }, records))
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
                headToHead ? ToMatchups(snapshot) : null);
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
        /// <returns>The team entry.</returns>
        private static TeamPayload ToTeam(TeamSeason team, IReadOnlyDictionary<int, TeamRecord>? records)
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
                records is null ? null : ToRecord(records.GetValueOrDefault(team.RosterId)));
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
            var games = HeadToHead.Games(rosters.SelectMany(t => t.Weeks).Where(w => w.Week == week));
            MatchupTeamPayload Side(TeamWeek w) => new(w.RosterId, names[w.RosterId], w.ActualPts);

            // Games is null when any id is null, so every paired side has one
            return games is null or []
                ? null
                : new MatchupsPayload(
                    week,
                    week <= snapshot.FinalThroughWeek,
                    [.. games.Select(g => new GamePayload(g.First.MatchupId!.Value, [Side(g.First), Side(g.Second)]))]);
        }

        /// <summary>
        /// Maps a roster's record for the page.
        /// </summary>
        /// <param name="record">The record, or null for a roster with no counted week yet.</param>
        /// <returns>The record, no wins and no losses before a week settles.</returns>
        private static RecordPayload ToRecord(TeamRecord? record) =>
            record is null
                ? new RecordPayload(0, 0, 0, 0m, 0m)
                : new RecordPayload(record.Wins, record.Losses, record.Ties, record.PointsFor, record.PointsAgainst);
    }
}
