using System.Globalization;

namespace FantasyLeague.Core
{
    /// <summary>
    /// The head-to-head ranking rule, as the page applies it (§10, T72a).
    /// </summary>
    public static class Standings
    {
        // culture-aware, as the page's localeCompare is: ordinal order would put "Bravo" before "alpha"
        private static readonly StringComparer Names = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: false);

        /// <summary>
        /// Ranks teams by win rate, a tie as half a win, then points for, then name.
        /// </summary>
        /// <typeparam name="T">Whatever carries a team's record and name.</typeparam>
        /// <param name="teams">The teams to rank.</param>
        /// <param name="record">A team's record.</param>
        /// <param name="name">A team's name, the last tiebreak and the whole order at 0-0.</param>
        /// <returns>The teams, first place first.</returns>
        public static IReadOnlyList<T> RankByRecord<T>(IEnumerable<T> teams, Func<T, TeamRecord> record, Func<T, string> name) =>
            [.. teams
                .OrderByDescending(t => WinRate(record(t)))
                .ThenByDescending(t => record(t).PointsFor)
                .ThenBy(name, Names)];

        /// <summary>
        /// Gets the points leaders, the champions of a total-points season (T70).
        /// </summary>
        /// <typeparam name="T">Whatever carries a team's points.</typeparam>
        /// <param name="teams">The table's teams, their points over the counted weeks only.</param>
        /// <param name="points">Over the counted weeks.</param>
        /// <returns>Everyone on the top score; none when nobody scored, as in a league read before its first game.</returns>
        public static IReadOnlyList<T> PointsLeaders<T>(IEnumerable<T> teams, Func<T, decimal> points) =>
            Leaders(teams, t => (0d, points(t)));

        /// <summary>
        /// Gets the record leaders, the champions of a head-to-head season with no playoffs (T70).
        /// </summary>
        /// <typeparam name="T">Whatever carries a team's record.</typeparam>
        /// <param name="teams">The table's teams, never the placeholder (A1).</param>
        /// <param name="record">Over the counted weeks.</param>
        /// <returns>
        /// Everyone level with the leader on win rate and points for: the name orders the
        /// table but never decides a title. None when nobody scored.
        /// </returns>
        public static IReadOnlyList<T> RecordLeaders<T>(IEnumerable<T> teams, Func<T, TeamRecord> record) =>
            Leaders(teams, t => (WinRate(record(t)), record(t).PointsFor));

        /// <summary>
        /// Gets everyone level with the best on what decides a place.
        /// </summary>
        /// <typeparam name="T">The team type.</typeparam>
        /// <param name="teams">The table's teams.</param>
        /// <param name="place">What decides a place, compared in order: a rate, then points.</param>
        /// <returns>The leaders; none when the best has no points.</returns>
        private static IReadOnlyList<T> Leaders<T>(IEnumerable<T> teams, Func<T, (double Rate, decimal Points)> place)
        {
            var placed = teams.Select(t => (Team: t, Place: place(t))).ToList();
            var best = placed.Select(p => p.Place).DefaultIfEmpty().Max();
            return best.Points <= 0m ? [] : [.. placed.Where(p => p.Place == best).Select(p => p.Team)];
        }

        /// <summary>
        /// Counts games behind the best wins less losses, a tie as half; never below zero.
        /// </summary>
        /// <param name="record">The team's record.</param>
        /// <param name="all">Every ranked team's record, this one included.</param>
        /// <returns>Games back; zero for the leader.</returns>
        /// <remarks>
        /// From the best margin, not the top row: a bye leaves teams on different game
        /// counts, so the win-rate leader need not have the best margin.
        /// </remarks>
        public static decimal GamesBack(TeamRecord record, IEnumerable<TeamRecord> all) =>
            (all.Max(Margin) - Margin(record)) / 2m;

        /// <summary>
        /// Wins over games, a tie as half a win; zero before any game.
        /// </summary>
        /// <param name="record">The record.</param>
        /// <returns>The win rate, computed in doubles as the page's is, so equal rates tie the same way.</returns>
        private static double WinRate(TeamRecord record) =>
            record.Games == 0 ? 0 : (record.Wins + (record.Ties / 2.0)) / record.Games;

        /// <summary>
        /// Wins less losses: what games back compares.
        /// </summary>
        /// <param name="record">The record.</param>
        /// <returns>The margin.</returns>
        private static int Margin(TeamRecord record) => record.Wins - record.Losses;
    }
}
