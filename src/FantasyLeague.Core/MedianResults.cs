namespace FantasyLeague.Core
{
    /// <summary>
    /// The median game's share of a record (Q12): one result a week against that week's median score.
    /// </summary>
    /// <param name="Wins">Weeks scored above the median.</param>
    /// <param name="Losses">Weeks scored below it.</param>
    /// <param name="Ties">Weeks scored exactly on it.</param>
    public sealed record MedianResults(int Wins, int Losses, int Ties);
}
