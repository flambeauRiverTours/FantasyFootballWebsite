namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Where the NFL calendar is, by Sleeper's reckoning.
    /// </summary>
    /// <param name="Season">The current season's year.</param>
    /// <param name="Week">The week Sleeper considers current.</param>
    public sealed record NFLState(int Season, int Week);
}
