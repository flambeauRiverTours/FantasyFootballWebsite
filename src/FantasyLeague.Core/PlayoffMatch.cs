namespace FantasyLeague.Core
{
    /// <summary>
    /// One match of the winners bracket, as Sleeper holds it (T69, A46).
    /// </summary>
    /// <param name="Round">1 for the first round.</param>
    /// <param name="Match">Sleeper's id, unique in the bracket.</param>
    /// <param name="First">Sleeper's <c>t1</c>.</param>
    /// <param name="Second">Sleeper's <c>t2</c>.</param>
    /// <param name="Winner">Set by Sleeper once it settles the match, with its own tiebreaks.</param>
    /// <param name="Placement">The place the match decides: 1 the final, 3, 5…; null for none.</param>
    public sealed record PlayoffMatch(int Round, int Match, BracketSide First, BracketSide Second, int? Winner, int? Placement);
}
