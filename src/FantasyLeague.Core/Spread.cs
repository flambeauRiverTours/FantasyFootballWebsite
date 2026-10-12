namespace FantasyLeague.Core
{
    /// <summary>
    /// How steadily a team's points arrived across its finalized weeks.
    /// </summary>
    /// <param name="Sigma">Population standard deviation (D1).</param>
    /// <param name="Cv">Sigma over the mean, comparable across scoring levels (D3). Null when the mean is zero, since there is nothing to divide by.</param>
    /// <param name="Mean">The average week.</param>
    /// <param name="Min">The lowest week.</param>
    /// <param name="Max">The highest week.</param>
    /// <param name="Weeks">How many weeks it covers.</param>
    public sealed record Spread(
        double Sigma, double? Cv, decimal Mean, decimal Min, decimal Max, int Weeks);

    /// <summary>
    /// Computes a <see cref="Spread"/>.
    /// </summary>
    public static class SpreadCalculator
    {
        /// <summary>
        /// Computes the spread of a team's finalized weeks.
        /// </summary>
        /// <param name="finalWeeks">Point totals for finalized weeks only.</param>
        /// <param name="minWeeks">The fewest weeks that give a meaningful answer.</param>
        /// <returns>The spread, or null below <paramref name="minWeeks"/> (D1).</returns>
        /// <remarks>
        /// Null rather than zero, so a two-week team never reads as the steadiest
        /// in the league.
        /// </remarks>
        public static Spread? Calculate(IReadOnlyList<decimal> finalWeeks, int minWeeks = 4)
        {
            // the empty check matters only when a caller passes minWeeks of zero
            if (finalWeeks.Count == 0 || finalWeeks.Count < minWeeks)
            {
                return null;
            }

            var mean = finalWeeks.Average();
            var meanD = (double)mean;
            var variance = finalWeeks.Sum(p => Math.Pow((double)p - meanD, 2)) / finalWeeks.Count;
            var sigma = Math.Sqrt(variance);

            // a zero mean with weeks swinging either side of it is not perfect consistency (F1)
            double? cv = meanD == 0 ? null : sigma / meanD;

            return new Spread(sigma, cv, mean, finalWeeks.Min(), finalWeeks.Max(), finalWeeks.Count);
        }
    }
}
