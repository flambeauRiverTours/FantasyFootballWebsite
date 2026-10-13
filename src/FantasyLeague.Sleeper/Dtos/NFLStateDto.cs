namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// <c>/v1/state/nfl</c> — what week it currently is.
    /// </summary>
    /// <param name="Season">The current season. Sleeper sends this as a string, e.g. <c>"2026"</c>; it binds to an int only because of <see cref="SleeperJson.Options"/>.</param>
    /// <param name="Week">The week Sleeper considers current.</param>
    internal sealed record NFLStateDto(
        int Season,
        int Week);
}
