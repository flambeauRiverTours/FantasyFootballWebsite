namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Where the NFL calendar is, by Sleeper's reckoning.
    /// </summary>
    /// <param name="Season">The current season's year.</param>
    /// <param name="Week">The week Sleeper considers current.</param>
    /// <param name="SeasonType"><c>"pre"</c>, <c>"regular"</c>, <c>"post"</c> or <c>"off"</c>; logged, not acted on (A37).</param>
    public sealed record NFLState(int Season, int Week, string? SeasonType);
}
