namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// <c>/v1/state/nfl</c> — what week it currently is.
    /// </summary>
    /// <param name="Season">The current season. Sleeper sends this as a string, e.g. <c>"2026"</c>; it binds to an int only because of <see cref="SleeperJson.Options"/>.</param>
    /// <param name="Week">The week Sleeper considers current.</param>
    /// <param name="SeasonType"><c>"pre"</c>, <c>"regular"</c>, <c>"post"</c> or <c>"off"</c>. Logged, not acted on, until its postseason behaviour has been observed (A37).</param>
    internal sealed record NFLStateDto(
        int Season,
        int Week,
        string? SeasonType);
}
