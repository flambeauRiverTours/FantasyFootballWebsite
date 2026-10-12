namespace FantasyLeague.Core
{
    /// <summary>
    /// The three fields of a Sleeper player that anything downstream actually
    /// needs, projected out of the 14 MB dump.
    /// </summary>
    /// <remarks>
    /// The daily refresh job caches a map of these and the season assembly reads
    /// it. The dump itself must never sit on a request path.
    /// </remarks>
    /// <param name="Name">The player's name, falling back to the id when Sleeper has none.</param>
    /// <param name="Team">The NFL team abbreviation, or null for a free agent.</param>
    /// <param name="FantasyPositions">Every position the player is eligible at. Never empty — entries without positions are dropped during projection, since nothing can be started at no position.</param>
    public sealed record PlayerRef(
        string Name,
        string? Team,
        IReadOnlyList<string> FantasyPositions);
}
