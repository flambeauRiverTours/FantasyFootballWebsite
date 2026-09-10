namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// <c>/v1/state/nfl</c> — what week it currently is.
    /// </summary>
    /// <param name="Season">The current season. Sleeper sends this as a string, e.g. <c>"2026"</c>; it binds to an int only because of <see cref="SleeperJson.Options"/>.</param>
    /// <param name="SeasonType">One of <c>pre</c>, <c>regular</c> or <c>post</c>. Q4 turns on whether post counts.</param>
    /// <param name="Week">The week Sleeper considers current.</param>
    /// <param name="DisplayWeek">The week shown in the app, which can lag <paramref name="Week"/> around a rollover.</param>
    /// <param name="PreviousSeason">The season before this one.</param>
    internal sealed record NflStateDto(
        int Season,
        string? SeasonType,
        int Week,
        int? DisplayWeek,
        int? PreviousSeason);
}
