namespace FantasyLeague.Seasons
{
    /// <summary>
    /// A team-week where one of the two invariants did not hold.
    /// </summary>
    /// <param name="Week">The week.</param>
    /// <param name="RosterId">The roster.</param>
    /// <param name="Invariant">Which check failed.</param>
    /// <param name="Expected">What it should have been.</param>
    /// <param name="Actual">What it was.</param>
    public sealed record InvariantViolation(
        int Week,
        int RosterId,
        InvariantKind Invariant,
        decimal Expected,
        decimal Actual);

    /// <summary>
    /// The two checks the payload is self-verifying against.
    /// </summary>
    public enum InvariantKind
    {
        /// <summary>sum(starters_points) == points, within a cent.</summary>
        StartersSumMatchesPoints,

        /// <summary>ideal &gt;= Sleeper's scored points, not a commissioner override. Catches restrictive eligibility only; a permissive slot map inflates ideal undetected.</summary>
        IdealNotBelowActual,
    }
}
