using System.Globalization;
using FantasyLeague.Core;
using FantasyLeague.Seasons;

namespace FantasyLeague.Cli
{
    /// <summary>
    /// Renders the season as a fixed-width table.
    /// </summary>
    /// <remarks>
    /// Every figure comes from the payload the site serves, so the console and
    /// the page cannot disagree. Only the verification footer reads the snapshot.
    /// </remarks>
    internal static class StandingsRenderer
    {
        private const int Name = 26;
        private const int Rule = 66;

        /// <summary>
        /// Writes the header, the standings and the verification footer.
        /// </summary>
        /// <param name="payload">The season as the site serves it, already ranked by points.</param>
        /// <param name="snapshot">The assembled season, for the invariant footer.</param>
        /// <param name="output">Where to write.</param>
        public static void Render(SeasonPayload payload, SeasonSnapshot snapshot, TextWriter output)
        {
            var teamWeeks = snapshot.Teams.Sum(t => t.Weeks.Count);
            // playoff weeks are stored for the bracket, so the invariants check them; the table does not
            // count them. Past the settings' cut-off is the playoffs; short of it, weeks nobody played (A37)
            var inPlayoffs = snapshot.CurrentWeek > snapshot.RegularSeasonCutoff;
            var checkedWeeks = inPlayoffs ? $"{teamWeeks} team-weeks, playoffs included" : $"{teamWeeks} team-weeks";
            var counted = Math.Min(payload.CurrentWeek, payload.LastRegularWeek);
            var after = payload.CurrentWeek > payload.LastRegularWeek
                ? $"   {(inPlayoffs ? "playoffs" : "weeks")} from wk {payload.LastRegularWeek + 1} not counted"
                : string.Empty;

            output.WriteLine(
                $"season {payload.Season}   {payload.Format}   weeks 1-{counted}   " +
                $"final through wk {payload.FinalThroughWeek}   {payload.AsOfUtc:yyyy-MM-dd HH:mm}Z{after}");
            output.WriteLine();

            // ranked as the page ranks: by record once every record is known (hasRecords), else by points
            if (payload.Format == LeagueFormat.HeadToHead && snapshot.Records is { } records)
            {
                ByRecord(payload, records, output);
            }
            else
            {
                ByPoints(payload, output);
            }

            output.WriteLine(new string('-', Rule));
            output.WriteLine();

            Footer(snapshot, checkedWeeks, output);
        }

        /// <summary>
        /// Whether the run should exit non-zero.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <returns>True on an invariant breach, an unresolved player or a record mismatch.</returns>
        public static bool Failed(SeasonSnapshot snapshot) =>
            snapshot.Violations.Count > 0
            || snapshot.UnresolvedPlayerIds.Count > 0
            || (snapshot.RecordCheck?.Mismatches.Count ?? 0) > 0;

        /// <summary>
        /// Writes the verification footer: the invariants, unresolved players, the record check and any breach.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <param name="checkedWeeks">What the invariants covered, for their lines.</param>
        /// <param name="output">Where to write.</param>
        private static void Footer(SeasonSnapshot snapshot, string checkedWeeks, TextWriter output)
        {
            var starters = Count(snapshot, InvariantKind.StartersSumMatchesPoints);
            var ideal = Count(snapshot, InvariantKind.IdealNotBelowActual);

            output.WriteLine($"invariants   starters==points  {Verdict(starters),-5}({checkedWeeks})");
            output.WriteLine($"             ideal>=points     {Verdict(ideal),-5}({checkedWeeks})");
            output.WriteLine($"unresolved   {(snapshot.UnresolvedPlayerIds.Count == 0 ? "none" : string.Join(", ", snapshot.UnresolvedPlayerIds))}");
            RecordCheckLines(snapshot.RecordCheck, output);

            if (snapshot.Violations.Count == 0)
            {
                return;
            }

            output.WriteLine();
            foreach (var v in snapshot.Violations.OrderBy(v => v.Week).ThenBy(v => v.RosterId))
            {
                output.WriteLine(
                    $"  wk {v.Week,-3} roster {v.RosterId,-3} {v.Invariant,-26} " +
                    $"sleeper {v.SleeperPts.ToString("F2", CultureInfo.InvariantCulture),9}  " +
                    $"compared {v.ComparedPts.ToString("F2", CultureInfo.InvariantCulture),9}");
            }
        }

        /// <summary>
        /// Writes the points table, in the payload's order.
        /// </summary>
        /// <param name="payload">The season, ranked by points.</param>
        /// <param name="output">Where to write.</param>
        private static void ByPoints(SeasonPayload payload, TextWriter output)
        {
            var lead = payload.Teams.FirstOrDefault()?.Totals.Actual ?? 0m;

            output.WriteLine($"{"TEAM",-Name}{"POINTS",9}{"SPREAD",8}{"IDEAL",9}{"EFF",6}{"GAP",8}");
            output.WriteLine(new string('-', Rule));

            foreach (var (name, (actual, ideal, sigma)) in payload.Teams.Select(t => (t.Name, (t.Totals.Actual, t.Totals.Ideal, t.Totals.Sigma))))
            {
                var spread = sigma is { } s ? s.ToString("F1", CultureInfo.InvariantCulture) : "-";
                var eff = ideal == 0 ? "-" : $"{(actual / ideal * 100m).ToString("F0", CultureInfo.InvariantCulture)}%";
                var gap = actual == lead ? "-" : F1(actual - lead);

                output.WriteLine($"{Fit(name, Name),-Name}{F1(actual),9}{spread,8}{F1(ideal),9}{eff,6}{gap,8}");
            }
        }

        /// <summary>
        /// Writes the head-to-head table: record, games back, points for and against, settled ideal.
        /// </summary>
        /// <param name="payload">The season, its teams the table's rows.</param>
        /// <param name="all">Every roster's record, the placeholder's included; a roster with no counted week has none.</param>
        /// <param name="output">Where to write.</param>
        private static void ByRecord(SeasonPayload payload, IReadOnlyDictionary<int, TeamRecord> all, TextWriter output)
        {
            // the table's teams only: the placeholder's record is checked, never shown or measured against (A1)
            var records = payload.Teams.ToDictionary(t => t.RosterId, t => all.GetValueOrDefault(t.RosterId) ?? new TeamRecord(0, 0, 0, 0m, 0m));
            var showTies = records.Values.Any(r => r.Ties > 0);

            output.WriteLine($"{"TEAM",-Name}{"RECORD",9}{"GB",6}{"PF",9}{"PA",8}{"IDEAL",8}");
            output.WriteLine(new string('-', Rule));

            foreach (var team in Standings.RankByRecord(payload.Teams, t => records[t.RosterId], t => t.Name))
            {
                var record = records[team.RosterId];
                var back = Standings.GamesBack(record, records.Values);
                var ideal = team.Weeks.Where(w => w.IsFinal).Sum(w => w.Ideal);

                output.WriteLine(
                    $"{Fit(team.Name, Name),-Name}{record.Format(showTies),9}{(back == 0 ? "-" : F1(back)),6}" +
                    $"{F1(record.PointsFor),9}{F1(record.PointsAgainst),8}{F1(ideal),8}");
            }
        }

        /// <summary>
        /// Writes the record check's line, then any mismatch with both records.
        /// </summary>
        /// <param name="check">The check, or null when our records are incomplete.</param>
        /// <param name="output">Where to write.</param>
        /// <remarks>Never OK when nothing was compared: the logs only show failures, so this line is the positive proof (A44).</remarks>
        private static void RecordCheckLines(RecordCheck? check, TextWriter output)
        {
            if (check is null)
            {
                output.WriteLine("record check not run (records incomplete)");
                return;
            }

            var skipped = check.Skipped == 0 ? string.Empty : $"; {check.Skipped} not, Sleeper has counted a different number of games";
            var line = check switch
            {
                { Compared: 0 } => $"not run (0 rosters compared{skipped})",
                { Mismatches.Count: 0 } => $"OK   ({check.Compared} rosters compared{skipped})",
                _ => $"FAIL ({check.Mismatches.Count} of {check.Compared} rosters compared disagree{skipped})",
            };
            output.WriteLine($"record check {line}");

            foreach (var m in check.Mismatches)
            {
                output.WriteLine($"  roster {m.RosterId,-3} ours {m.OurRecord.Format()}   sleeper {m.SleeperRecord.Format()}");
            }
        }

        /// <summary>
        /// Formats a figure to one decimal, whatever the ambient culture.
        /// </summary>
        /// <returns>The figure.</returns>
        private static string F1(decimal value) => value.ToString("F1", CultureInfo.InvariantCulture);

        /// <summary>
        /// Counts violations of one invariant.
        /// </summary>
        /// <returns>How many team-weeks failed it, excluded rosters included.</returns>
        private static int Count(SeasonSnapshot snapshot, InvariantKind kind) =>
            snapshot.Violations.Count(v => v.Invariant == kind);

        /// <summary>
        /// Renders a pass or fail marker.
        /// </summary>
        /// <param name="failures">How many failed.</param>
        /// <returns>The marker.</returns>
        private static string Verdict(int failures) => failures == 0 ? "OK" : "FAIL";

        /// <summary>
        /// Truncates a name so the columns cannot move.
        /// </summary>
        /// <returns>The name, cut with a trailing <c>~</c> when it would reach the next column.</returns>
        private static string Fit(string value, int width) =>
            value.Length <= width - 1 ? value : value[..(width - 2)] + "~";
    }
}
