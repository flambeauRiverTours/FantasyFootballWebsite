namespace FantasyLeague.Core
{
    /// <summary>
    /// One side of a playoff match (T69).
    /// </summary>
    /// <param name="RosterId">Null until Sleeper knows who plays.</param>
    /// <param name="From">Where the side comes from, once its source match is played.</param>
    /// <param name="FromMatch">That match's id; 0 with <see cref="BracketSource.None"/>.</param>
    public sealed record BracketSide(int? RosterId, BracketSource From, int FromMatch);
}
