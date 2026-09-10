namespace FantasyLeague.Core
{
    /// <summary>
    /// Represents a fantasy team's performance for a specific week, including the roster ID, week number, actual and ideal points scored, starters, ideal lineup, and whether the week's results are final.
    /// </summary>
    /// <param name="RosterId">The ID of the team's roster.</param>
    /// <param name="Week">The week number.</param>
    /// <param name="Actual">The actual points scored by the team.</param>
    /// <param name="Ideal">The ideal points scored by the team.</param>
    /// <param name="Starters">The list of players who started the week.</param>
    /// <param name="IdealLineup">The ideal lineup for the week.</param>
    /// <param name="IsFinal">Indicates whether the week's results are final.</param>
    public sealed record TeamWeek(
    int RosterId, int Week,
    decimal Actual, decimal Ideal,
    IReadOnlyList<PlayerId> Starters,
    IReadOnlyList<PlayerId> IdealLineup,
    bool IsFinal);
}
