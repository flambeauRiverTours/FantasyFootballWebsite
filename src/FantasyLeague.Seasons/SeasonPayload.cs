using System.Text.Json.Serialization;

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
    /// <param name="LastRegularWeek">The last week that counts (A37). Once <paramref name="FinalThroughWeek"/> reaches it the season is complete.</param>
    /// <param name="Format">How the league decides its winner (A38).</param>
    /// <param name="Matchups">
    /// The games of <see cref="SeasonSnapshot.MatchupWeek"/>, in <see cref="LeagueFormat.HeadToHead"/>
    /// only and only when that week has games (T71, A41). Left out of the body otherwise.
    /// </param>
    public sealed record SeasonPayload(
        string LeagueName,
        int Season,
        bool IsCurrent,
        int CurrentWeek,
        int FinalThroughWeek,
        DateTimeOffset AsOfUtc,
        IReadOnlyList<string> SlotOrder,
        IReadOnlyDictionary<string, decimal> LeagueSlotAverages,
        IReadOnlyList<TeamPayload> Teams,
        int LastRegularWeek,
        LeagueFormat Format,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MatchupsPayload? Matchups = null);

    /// <summary>
    /// One week's games (T71).
    /// </summary>
    /// <param name="Week">The current week, held to the last counted week (A41).</param>
    /// <param name="IsFinal">Whether that week is settled; until then the points are live.</param>
    /// <param name="Games">Each game, in matchup id order.</param>
    public sealed record MatchupsPayload(
        int Week,
        bool IsFinal,
        IReadOnlyList<GamePayload> Games);

    /// <summary>
    /// One game, never a bye: a game always has two sides.
    /// </summary>
    /// <param name="MatchupId">Sleeper's id for the game, unique within the week.</param>
    /// <param name="Teams">Its two sides, in roster id order, so they never swap between polls.</param>
    public sealed record GamePayload(
        int MatchupId,
        IReadOnlyList<MatchupTeamPayload> Teams);

    /// <summary>
    /// One side of a game. The placeholder's side carries its name, since it has no table row (A1).
    /// </summary>
    /// <param name="RosterId">Matches a team's <see cref="TeamPayload.RosterId"/>; the placeholder's matches none (A1).</param>
    /// <param name="Name">The team name, carried because the placeholder has no table row.</param>
    /// <param name="Actual">What it has scored that week so far.</param>
    public sealed record MatchupTeamPayload(
        int RosterId,
        string Name,
        decimal Actual);

    /// <summary>
    /// One roster's season.
    /// </summary>
    /// <param name="RosterId">The roster's id within this league and season.</param>
    /// <param name="Name">The team name, falling back to the manager's handle.</param>
    /// <param name="Totals">Season-to-date figures, computed server-side so the definition lives in one place.</param>
    /// <param name="SlotAverages">Per-slot season means. Empty if no week produced an attribution.</param>
    /// <param name="Weeks">Every week played, in order.</param>
    /// <param name="Record">
    /// The head-to-head record, in <see cref="LeagueFormat.HeadToHead"/> once every counted week
    /// has its games (T68). Left out of the body otherwise, so a total-points body carries no
    /// <c>record</c> key (A39).
    /// </param>
    public sealed record TeamPayload(
        int RosterId,
        string Name,
        TotalsPayload Totals,
        IReadOnlyDictionary<string, decimal> SlotAverages,
        IReadOnlyList<WeekPayload> Weeks,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RecordPayload? Record = null);

    /// <summary>
    /// One roster's head-to-head record over the settled weeks that count (T68).
    /// </summary>
    /// <param name="Wins">Games won, median results included.</param>
    /// <param name="Losses">Games lost, median results included.</param>
    /// <param name="Ties">Games tied, median results included.</param>
    /// <param name="PointsFor">Points over every settled week that counts, a bye included; the tiebreak after win rate.</param>
    /// <param name="PointsAgainst">Opponents' points over the same weeks.</param>
    public sealed record RecordPayload(
        int Wins,
        int Losses,
        int Ties,
        decimal PointsFor,
        decimal PointsAgainst);

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
