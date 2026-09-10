namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One entry from the <c>/v1/players/nfl</c> dump, which is a map of player id
    /// to player and runs to roughly 14 MB across about 12,200 entries.
    /// </summary>
    /// <remarks>
    /// Only three fields survive the projection to <see cref="PlayerRef"/>. The
    /// dump is dirtier than it looks: around 330 entries carry no
    /// <see cref="FantasyPositions"/> and around 30 no <see cref="FullName"/>, so
    /// both are treated as optional rather than assumed.
    /// </remarks>
    /// <param name="PlayerId">Sleeper's id. Numeric-looking for skill players, a team abbreviation such as <c>"BAL"</c> for a defence — never parse it as a number.</param>
    /// <param name="FullName">The player's name, or null. Defences carry no full name.</param>
    /// <param name="Team">The NFL team abbreviation, or null for a free agent.</param>
    /// <param name="Position">The player's single nominal position. Not what eligibility is matched on.</param>
    /// <param name="FantasyPositions">Every position the player is fantasy-eligible at. This is the field slot eligibility matches against; using <paramref name="Position"/> instead silently undercounts multi-eligible players.</param>
    internal sealed record SleeperPlayerDto(
        string? PlayerId,
        string? FullName,
        string? Team,
        string? Position,
        List<string>? FantasyPositions);
}
