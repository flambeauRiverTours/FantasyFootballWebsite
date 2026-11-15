namespace FantasyLeague.Core
{
    /// <summary>
    /// A roster's head-to-head record over the weeks counted (T68).
    /// </summary>
    /// <param name="Wins">Games won, median results included (Q12).</param>
    /// <param name="Losses">Games lost, median results included.</param>
    /// <param name="Ties">Games tied, median results included.</param>
    /// <param name="PointsFor">Points scored over the same weeks; the tiebreak after win rate.</param>
    /// <param name="PointsAgainst">Opponents' points over the same weeks. Median results add none.</param>
    public sealed record TeamRecord(int Wins, int Losses, int Ties, decimal PointsFor, decimal PointsAgainst)
    {
        /// <summary>
        /// Gets how many results the record holds. Two a week with a median game.
        /// </summary>
        public int Games => Wins + Losses + Ties;
    }
}
