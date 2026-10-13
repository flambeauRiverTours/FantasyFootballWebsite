namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One entry from the <c>/v1/players/nfl</c> dump, which is a map of player id
    /// to player and runs to roughly 14 MB across about 12,200 entries.
    /// </summary>
    /// <remarks>
    /// Only three fields survive the projection to <see cref="FantasyLeague.Core.PlayerRef"/>. The
    /// dump is dirtier than it looks: around 330 entries carry no
    /// <see cref="FantasyPositions"/> and around 30 no <see cref="FullName"/>, so
    /// both are treated as optional rather than assumed.
    /// </remarks>
    /// <param name="FullName">The player's name, or null. Defences carry no full name.</param>
    /// <param name="Team">The NFL team abbreviation, or null for a free agent.</param>
    /// <param name="FantasyPositions">Every position the player is fantasy-eligible at. This is the field slot eligibility matches against; the singular <c>position</c> would silently undercount multi-eligible players.</param>
    internal sealed record SleeperPlayerDto(
        string? FullName,
        string? Team,
        IReadOnlyList<string>? FantasyPositions);
}
