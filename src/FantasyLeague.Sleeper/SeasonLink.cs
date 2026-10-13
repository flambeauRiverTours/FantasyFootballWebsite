namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One league's place in the season chain (D8).
    /// </summary>
    /// <param name="Season">The season the league plays.</param>
    /// <param name="PreviousLeagueId">The league it rolled over from, or null for the first season.</param>
    public sealed record SeasonLink(int Season, string? PreviousLeagueId);
}
