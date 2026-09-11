using System.Globalization;
using FantasyLeague.Core;
using FantasyLeague.Sleeper;

namespace FantasyLeague.Console
{
    /// <summary>
    /// Renders a season snapshot as a fixed-width table.
    /// </summary>
    internal static class StandingsRenderer
    {
        private const int Name = 26;
        private const int Rule = 65;

        /// <summary>
        /// Writes the header, the standings and the verification footer.
        /// </summary>
        /// <param name="snapshot">The season to render.</param>
        /// <param name="output">Where to write.</param>
        /// <param name="asOfUtc">Timestamp for the header.</param>
        public static void Render(SeasonSnapshot snapshot, TextWriter output, DateTimeOffset asOfUtc)
        {
            var rows = snapshot.Teams
                .Select(t => new
                {
                    t.Name,
                    Points = t.Weeks.Sum(w => w.Actual),
                    Ideal = t.Weeks.Sum(w => w.Ideal),
                    Spread = SpreadCalculator.Calculate([.. t.Weeks.Where(w => w.IsFinal).Select(w => w.Actual)]),
                })
                .OrderByDescending(r => r.Points)
                .ToList();

            var lead = rows.Count == 0 ? 0m : rows[0].Points;
            var teamWeeks = snapshot.Teams.Sum(t => t.Weeks.Count);

            output.WriteLine(
                $"season {snapshot.Season}   weeks 1-{snapshot.CurrentWeek}   " +
                $"final through wk {snapshot.FinalThroughWeek}   {asOfUtc:yyyy-MM-dd HH:mm}Z");
            output.WriteLine();
            output.WriteLine($"{"TEAM",-Name}{"POINTS",9}{"SD",7}{"IDEAL",9}{"EFF",6}{"GAP",8}");
            output.WriteLine(new string('-', Rule));

            foreach (var r in rows)
            {
                var sd = r.Spread is { } s ? s.Sigma.ToString("F1", CultureInfo.InvariantCulture) : "-";
                var eff = r.Ideal == 0 ? "-" : $"{(r.Points / r.Ideal * 100m).ToString("F0", CultureInfo.InvariantCulture)}%";
                var gap = r.Points == lead ? "-" : (r.Points - lead).ToString("F1", CultureInfo.InvariantCulture);

                output.WriteLine(
                    $"{Fit(r.Name, Name),-Name}" +
                    $"{r.Points.ToString("F1", CultureInfo.InvariantCulture),9}" +
                    $"{sd,7}" +
                    $"{r.Ideal.ToString("F1", CultureInfo.InvariantCulture),9}" +
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
