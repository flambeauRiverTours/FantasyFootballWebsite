namespace FantasyLeague.Core
{
    public sealed record Spread(
    double Sigma, double Cv, decimal Mean, decimal Min, decimal Max, int Weeks);

    public static class SpreadCalculator
    {
        /// <summary>
        /// Calculates the spread of a list of final weeks. Returns null if the number of weeks is less than the minimum number of weeks.
        /// </summary>
        /// <param name="finalWeeks">Point totals for weeks that have been completed</param>
        /// <param name="minWeeks">The minimum number of weeks required to calculate the spread</param>
        /// <returns>The spread of the final weeks, or null if there are not enough weeks</returns>
        public static Spread? Calculate(IReadOnlyList<decimal> finalWeeks, int minWeeks = 4)
        {
            if (finalWeeks.Count < minWeeks)
            {
                return null;
            }

            var mean = finalWeeks.Average();
            var meanD = (double)mean;
            var variance = finalWeeks.Sum(p => Math.Pow((double)p - meanD, 2)) / finalWeeks.Count;
            var sigma = Math.Sqrt(variance);

            return new Spread(sigma, meanD == 0 ? 0 : sigma / meanD,
                              mean, finalWeeks.Min(), finalWeeks.Max(), finalWeeks.Count);
        }
    }
}
