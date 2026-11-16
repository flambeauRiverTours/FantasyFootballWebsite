namespace FantasyLeague.Core
{
    /// <summary>
    /// A roster's head-to-head record over the weeks counted (T68).
    /// </summary>
    /// <param name="Wins">Games won, median results included (Q12).</param>
    /// <param name="Losses">Games lost, median results included.</param>
    /// <param name="Ties">Games tied, median results included.</param>
    /// <param name="PointsFor">Points over every counted week, a bye included, as Sleeper's are; the tiebreak after win rate.</param>
    /// <param name="PointsAgainst">Opponents' points over the same weeks. Median results add none.</param>
    /// <param name="Median">The median game's share; null without one (A43).</param>
    public sealed record TeamRecord(int Wins, int Losses, int Ties, decimal PointsFor, decimal PointsAgainst, MedianResults? Median = null)
    {
        /// <summary>
        /// Gets how many results the record holds. Two a week with a median game.
        /// </summary>
        public int Games => Wins + Losses + Ties;
    }
}
