namespace FantasyLeague.Core
{
    /// <summary>
    /// One roster's week: what it scored, what it could have scored, and who played.
    /// </summary>
    /// <param name="RosterId">Sleeper's roster id.</param>
    /// <param name="Week">The week number.</param>
    /// <param name="ActualPts">The points the team scored, with any commissioner override applied.</param>
    /// <param name="IdealPts">The points the best legal lineup from that week's roster would have scored.</param>
    /// <param name="Starters">Who was started, in slot order, empty slots omitted.</param>
    /// <param name="IdealLineup">Who the solver would have started.</param>
    /// <param name="Players">Every player rostered that week, with the points they scored.</param>
    /// <param name="IsFinal">Whether the week's scores are settled.</param>
    /// <param name="SlotAverages">Average points per slot. Empty when the mid-season rules guard excluded the week.</param>
    public sealed record TeamWeek(
        int RosterId,
        int Week,
        decimal ActualPts,
        decimal IdealPts,
        IReadOnlyList<PlayerId> Starters,
        IReadOnlyList<PlayerId> IdealLineup,
        IReadOnlyList<RosteredPlayer> Players,
        bool IsFinal,
        IReadOnlyDictionary<string, decimal> SlotAverages);
}
