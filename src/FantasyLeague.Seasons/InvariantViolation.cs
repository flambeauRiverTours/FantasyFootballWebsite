namespace FantasyLeague.Seasons
{
    /// <summary>
    /// A team-week where one of the two invariants did not hold.
    /// </summary>
    /// <param name="Week">The week.</param>
    /// <param name="RosterId">The roster.</param>
    /// <param name="Invariant">Which check failed.</param>
    /// <param name="SleeperPts">Sleeper's scored points, before any override, which both checks compare against (A9).</param>
    /// <param name="ComparedPts">
    /// What was checked against them: the starters' sum, which should equal them,
    /// or the ideal points, which should be no lower.
    /// </param>
    public sealed record InvariantViolation(
        int Week,
        int RosterId,
        InvariantKind Invariant,
        decimal SleeperPts,
        decimal ComparedPts);

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
