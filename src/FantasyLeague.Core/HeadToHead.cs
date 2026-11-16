namespace FantasyLeague.Core
{
    /// <summary>
    /// Head-to-head records from each week's games (T68).
    /// </summary>
    public static class HeadToHead
    {
        /// <summary>
        /// Builds every roster's record over the settled weeks that count.
        /// </summary>
        /// <param name="weeks">Every roster's weeks, the excluded rosters' included: a game against the placeholder counts.</param>
        /// <param name="lastWeek">The last week that counts (A37), after unplayed weeks are trimmed; a trimmed week's 0-0 games would count as ties.</param>
        /// <param name="hasMedianGame">Whether each week adds a result against that week's median score (Q12).</param>
        /// <returns>
        /// A record per roster, or null when any counted week lacks its games: a
        /// week stored before matchups were read, or a game that does not pair
        /// exactly two rosters. A partial record would rank teams wrongly.
        /// </returns>
        /// <remarks>
        /// The higher score wins and equal scores tie. A bye (matchup 0) is no game.
        /// The median is taken over every roster with a row that week, as Sleeper
        /// does: above it wins, below it loses, on it ties. Only settled weeks
        /// count (R1), so the live week never moves a record.
        /// </remarks>
        public static IReadOnlyDictionary<int, TeamRecord>? Records(IEnumerable<TeamWeek> weeks, int lastWeek, bool hasMedianGame)
        {
            var counted = weeks.Where(w => w.IsFinal && w.Week <= lastWeek).ToList();
            var games = Games(counted);
            if (games is null)
            {
                return null;
            }

            var tally = counted.Select(w => w.RosterId).Distinct().ToDictionary(id => id, _ => new Tally());
            foreach (var w in counted)
            {
                tally[w.RosterId].Score(w.ActualPts);
            }

            foreach (var (a, b) in games)
            {
                tally[a.RosterId].Add(a.ActualPts, b.ActualPts);
                tally[b.RosterId].Add(b.ActualPts, a.ActualPts);
            }

            if (hasMedianGame)
            {
                foreach (var week in counted.GroupBy(w => w.Week))
                {
                    var median = Median(week.Select(w => w.ActualPts));
                    foreach (var w in week)
                    {
                        tally[w.RosterId].AddResult(w.ActualPts.CompareTo(median));
                    }
                }
            }

            return tally.ToDictionary(t => t.Key, t => t.Value.ToRecord());
        }

        /// <summary>
        /// Pairs the weeks into games: the one rule both the records and the matchups panel use (T71).
        /// </summary>
        /// <param name="weeks">The weeks to pair, the excluded rosters' included.</param>
        /// <returns>
        /// Each game's two sides, by week then matchup id, sides in roster id order so a
        /// change of lead never reorders them; or null when a week has no matchup id, or
        /// a game does not pair exactly two rosters. A bye (matchup 0) is no game.
        /// </returns>
        public static IReadOnlyList<(TeamWeek First, TeamWeek Second)>? Games(IEnumerable<TeamWeek> weeks)
        {
            var list = weeks.ToList();
            if (list.Any(w => w.MatchupId is null))
            {
                return null;
            }

            var games = list.Where(w => w.MatchupId > 0)
                .GroupBy(w => (w.Week, w.MatchupId))
                .OrderBy(g => g.Key)
                .Select(g => g.OrderBy(w => w.RosterId).ToList())
                .ToList();
            return games.Any(g => g.Count != 2) ? null : [.. games.Select(g => (g[0], g[1]))];
        }

        /// <summary>
        /// Takes the middle score, or the mean of the middle two.
        /// </summary>
        /// <param name="scores">One week's scores, at least one.</param>
        /// <returns>The median.</returns>
        private static decimal Median(IEnumerable<decimal> scores)
        {
            var sorted = scores.Order().ToList();
            var middle = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2m;
        }

        /// <summary>
        /// One roster's running record.
        /// </summary>
        private sealed class Tally
        {
            private int wins;
            private int losses;
            private int ties;
            private decimal pointsFor;
            private decimal pointsAgainst;

            /// <summary>
            /// Adds a counted week's points, a bye's included, as Sleeper's points for does.
            /// </summary>
            /// <param name="scored">This roster's points that week.</param>
            public void Score(decimal scored) => pointsFor += scored;

            /// <summary>
            /// Adds a game: its result and the opponent's points.
            /// </summary>
            /// <param name="scored">This roster's points, which decide the result.</param>
            /// <param name="conceded">The opponent's points.</param>
            public void Add(decimal scored, decimal conceded)
            {
                pointsAgainst += conceded;
                AddResult(scored.CompareTo(conceded));
            }

            /// <summary>
            /// Adds a result alone, as the median game does.
            /// </summary>
            /// <param name="comparison">Positive for a win, negative for a loss, zero for a tie.</param>
            public void AddResult(int comparison)
            {
                if (comparison > 0)
                {
                    wins++;
                }
                else if (comparison < 0)
                {
                    losses++;
                }
                else
                {
                    ties++;
                }
            }

            /// <summary>
            /// Freezes the tally.
            /// </summary>
            /// <returns>The results and points so far.</returns>
            public TeamRecord ToRecord() => new(wins, losses, ties, pointsFor, pointsAgainst);
        }
    }
}
