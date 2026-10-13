namespace FantasyLeague.Seasons
{
    /// <summary>
    /// The §09 response body for one season. Table and chart derive from this one
    /// payload, so they cannot disagree.
    /// </summary>
    /// <param name="LeagueName">The league's name, after any configured override.</param>
    /// <param name="Season">The season these weeks belong to.</param>
    /// <param name="IsCurrent">Whether this is the live season. Decides the cache headers and whether the page polls.</param>
    /// <param name="CurrentWeek">The week Sleeper considers current.</param>
    /// <param name="FinalThroughWeek">The last settled week. Lets the table label the sigma column honestly while points already include the live week.</param>
    /// <param name="AsOfUtc">When the snapshot was assembled.</param>
    /// <param name="SlotOrder">Distinct startable slots in roster_positions order. Fixed, never re-sorted (R2).</param>
    /// <param name="LeagueSlotAverages">The radar's reference circle, averaged over non-excluded teams (A1).</param>
    /// <param name="Teams">One entry per scoring roster.</param>
    public sealed record SeasonPayload(
        string LeagueName,
        int Season,
        bool IsCurrent,
        int CurrentWeek,
        int FinalThroughWeek,
        DateTimeOffset AsOfUtc,
        IReadOnlyList<string> SlotOrder,
        IReadOnlyDictionary<string, decimal> LeagueSlotAverages,
        IReadOnlyList<TeamPayload> Teams);

    /// <summary>
    /// One roster's season.
    /// </summary>
    /// <param name="RosterId">The roster's id within this league and season.</param>
    /// <param name="Name">The team name, falling back to the manager's handle.</param>
    /// <param name="Totals">Season-to-date figures, computed server-side so the definition lives in one place.</param>
    /// <param name="SlotAverages">Per-slot season means. Empty if no week produced an attribution.</param>
    /// <param name="Weeks">Every week played, in order.</param>
    public sealed record TeamPayload(
        int RosterId,
        string Name,
        TotalsPayload Totals,
        IReadOnlyDictionary<string, decimal> SlotAverages,
        IReadOnlyList<WeekPayload> Weeks);

    /// <summary>
    /// Season-to-date figures for one roster.
    /// </summary>
    /// <param name="Actual">Sum of every week's score, including the live one. This is what decides the league.</param>
    /// <param name="Ideal">Sum of the best obtainable lineup each week.</param>
    /// <param name="Sigma">Population deviation over finalized weeks. Null below four (D1).</param>
    /// <param name="Cv">Sigma over the mean. Null whenever sigma is.</param>
    /// <param name="Mean">Mean finalized weekly score.</param>
    /// <param name="Min">Lowest finalized weekly score.</param>
    /// <param name="Max">Highest finalized weekly score.</param>
    public sealed record TotalsPayload(
        decimal Actual,
        decimal Ideal,
        double? Sigma,
        double? Cv,
        decimal? Mean,
        decimal? Min,
        decimal? Max);

    /// <summary>
    /// One week of one roster.
    /// </summary>
    /// <param name="Week">The week number.</param>
    /// <param name="Actual">What the roster scored.</param>
    /// <param name="Ideal">What it could have scored.</param>
    /// <param name="IsFinal">Whether the week is settled. Only these count toward sigma.</param>
    public sealed record WeekPayload(
        int Week,
        decimal Actual,
        decimal Ideal,
        bool IsFinal);
}
