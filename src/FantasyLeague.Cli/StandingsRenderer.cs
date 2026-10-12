using System.Globalization;
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
        /// <param name="payload">The season as the site serves it, already ranked.</param>
        /// <param name="snapshot">The assembled season, for the invariant footer.</param>
        /// <param name="output">Where to write.</param>
        public static void Render(SeasonPayload payload, SeasonSnapshot snapshot, TextWriter output)
        {
            var rows = payload.Teams
                .Select(t => new
                {
                    t.Name,
                    ActualPts = t.Totals.Actual,
                    IdealPts = t.Totals.Ideal,
                    t.Totals.Sigma,
                })
                .ToList();

            var lead = rows.Count == 0 ? 0m : rows[0].ActualPts;
            var teamWeeks = snapshot.Teams.Sum(t => t.Weeks.Count);

            output.WriteLine(
                $"season {payload.Season}   weeks 1-{payload.CurrentWeek}   " +
                $"final through wk {payload.FinalThroughWeek}   {payload.AsOfUtc:yyyy-MM-dd HH:mm}Z");
            output.WriteLine();
            output.WriteLine($"{"TEAM",-Name}{"POINTS",9}{"SPREAD",8}{"IDEAL",9}{"EFF",6}{"GAP",8}");
            output.WriteLine(new string('-', Rule));

            foreach (var r in rows)
            {
                var spread = r.Sigma is { } s ? s.ToString("F1", CultureInfo.InvariantCulture) : "-";
                var eff = r.IdealPts == 0 ? "-" : $"{(r.ActualPts / r.IdealPts * 100m).ToString("F0", CultureInfo.InvariantCulture)}%";
                var gap = r.ActualPts == lead ? "-" : (r.ActualPts - lead).ToString("F1", CultureInfo.InvariantCulture);

                output.WriteLine(
                    $"{Fit(r.Name, Name),-Name}" +
                    $"{r.ActualPts.ToString("F1", CultureInfo.InvariantCulture),9}" +
                    $"{spread,8}" +
                    $"{r.IdealPts.ToString("F1", CultureInfo.InvariantCulture),9}" +
                    $"{eff,6}{gap,8}");
            }

            output.WriteLine(new string('-', Rule));
            output.WriteLine();

            var starters = Count(snapshot, InvariantKind.StartersSumMatchesPoints);
            var ideal = Count(snapshot, InvariantKind.IdealNotBelowActual);

            output.WriteLine($"invariants   starters==points  {Verdict(starters),-5}({teamWeeks} team-weeks)");
            output.WriteLine($"             ideal>=points     {Verdict(ideal),-5}({teamWeeks} team-weeks)");
            output.WriteLine($"unresolved   {(snapshot.UnresolvedPlayerIds.Count == 0 ? "none" : string.Join(", ", snapshot.UnresolvedPlayerIds))}");

            if (snapshot.Violations.Count == 0)
            {
                return;
            }

            output.WriteLine();
            foreach (var v in snapshot.Violations.OrderBy(v => v.Week).ThenBy(v => v.RosterId))
            {
                output.WriteLine(
                    $"  wk {v.Week,-3} roster {v.RosterId,-3} {v.Invariant,-26} " +
                    $"expected {v.Expected.ToString("F2", CultureInfo.InvariantCulture),9}  " +
                    $"actual {v.Actual.ToString("F2", CultureInfo.InvariantCulture),9}");
            }
        }

        /// <summary>
        /// Counts violations of one invariant.
        /// </summary>
        /// <param name="snapshot">The season.</param>
        /// <param name="kind">Which invariant.</param>
        /// <returns>How many failed.</returns>
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
        /// <param name="value">The name.</param>
        /// <param name="width">The column width.</param>
        /// <returns>A name no wider than the column.</returns>
        private static string Fit(string value, int width) =>
            value.Length <= width - 1 ? value : value[..(width - 2)] + "~";
    }
}
