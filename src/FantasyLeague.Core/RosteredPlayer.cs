namespace FantasyLeague.Core
{
    /// <summary>
    /// Represents a player that is rostered on a fantasy team, including their unique identifier, name, eligible fantasy positions, and total points scored.
    /// </summary>
    /// <param name="Id">The unique identifier for the player.</param>
    /// <param name="Name">The name of the player.</param>
    /// <param name="FantasyPositions">The list of fantasy positions the player can play.</param>
    /// <param name="Points">The total points scored by the player.</param>
    public sealed record RosteredPlayer(
    PlayerId Id,
    string Name,
    IReadOnlyList<string> FantasyPositions,
    decimal Points);

    /// <summary>
    /// Represents a unique identifier for a player in the fantasy league system. This record struct is immutable and provides a simple way to encapsulate the player's ID as a string.
    /// </summary>
    /// <param name="Value">The value of the player ID.</param>
    public readonly record struct PlayerId(string Value);
}
