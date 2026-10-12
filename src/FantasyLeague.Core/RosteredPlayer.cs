namespace FantasyLeague.Core
{
    /// <summary>
    /// One player on a roster in one week, as the solver sees them.
    /// </summary>
    /// <param name="Id">Sleeper's player id.</param>
    /// <param name="Name">The player's name.</param>
    /// <param name="FantasyPositions">Every position the player may start at, which is what flex eligibility checks.</param>
    /// <param name="Points">What the player scored that week, zero when Sleeper has no entry.</param>
    public sealed record RosteredPlayer(
        PlayerId Id,
        string Name,
        IReadOnlyList<string> FantasyPositions,
        decimal Points);

    /// <summary>
    /// Sleeper's player id: numeric for most players, a team abbreviation such as <c>DEN</c> for a defence.
    /// </summary>
    /// <param name="Value">The id.</param>
    public readonly record struct PlayerId(string Value);
}
