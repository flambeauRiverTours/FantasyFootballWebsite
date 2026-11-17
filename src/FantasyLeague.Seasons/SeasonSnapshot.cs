using System.ComponentModel;
using FantasyLeague.Core;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// One league season, assembled from Sleeper and ready for a caller to shape.
    /// </summary>
    /// <param name="LeagueName">The league's name, after any configured override.</param>
    /// <param name="Season">The season these weeks belong to.</param>
    /// <param name="CurrentWeek">The week Sleeper considers current.</param>
    /// <param name="FinalThroughWeek">The last week whose scores are settled. Sigma covers only these.</param>
    /// <param name="Slots">Startable slots in roster_positions order, for this season only.</param>
    /// <param name="Teams">Every roster that counts. A roster excluded by A1 is never here.</param>
    /// <param name="UnresolvedPlayerIds">Rostered ids the player map could not explain, excluded rosters included.</param>
    /// <param name="Violations">Invariant breaches found while assembling, excluded rosters included.</param>
    /// <param name="IsCurrent">Whether this is the live season; false for a completed one (A34).</param>
    /// <param name="RegularSeasonCutoff">The last week the league's settings allow to count (A37): the week before the playoffs, else 18. The payload's last counted week can be earlier, once unplayed weeks are trimmed. <see cref="Teams"/> holds playoff weeks too, so they are stored; the payload leaves them out.</param>
    /// <param name="Format">The deployment's format, applied to every season, past ones included (Q10, A38).</param>
    /// <param name="ExcludedTeams">The rosters A1 leaves out of every metric, kept apart so nothing that counts reads them. Stored, and paired in head-to-head, since a game against the placeholder counts (T68).</param>
    /// <param name="HasMedianGame">Whether each week adds a result against that week's median score (Q12, T68).</param>
    /// <param name="Bracket">The winners bracket (T69), or null when not read: not a playoff league in head-to-head, before the playoffs, or the read failed.</param>
    /// <param name="PlayoffRoundType">Sleeper's <c>playoff_round_type</c>; see <see cref="Playoffs.RoundWeeks"/>.</param>
    /// <param name="RecordCheck">Our records against Sleeper's (T68), mismatches reported like <see cref="Violations"/>; null until run, or when our records are incomplete.</param>
    /// <remarks>
    /// An excluded roster is left out of every metric but still checked: a
    /// solver bug shows on the placeholder team's lineup as well as any other.
    /// Marked immutable so the cache hands every caller the same instance
    /// rather than a serialized copy.
    /// </remarks>
    [ImmutableObject(true)]
    public sealed record SeasonSnapshot(
        string LeagueName,
        int Season,
        int CurrentWeek,
        int FinalThroughWeek,
        IReadOnlyList<Slot> Slots,
        IReadOnlyList<TeamSeason> Teams,
        IReadOnlyList<string> UnresolvedPlayerIds,
        IReadOnlyList<InvariantViolation> Violations,
        bool IsCurrent,
        int RegularSeasonCutoff,
        LeagueFormat Format,
        IReadOnlyList<TeamSeason> ExcludedTeams,
        bool HasMedianGame,
        RecordCheck? RecordCheck,
        IReadOnlyList<PlayoffMatch>? Bracket = null,
        int PlayoffRoundType = 0,
        IReadOnlyList<Division>? Divisions = null)
    {
        /// <summary>
        /// Gets the divisions the standings are grouped by (T72b, A48): head-to-head only (Q14), and
        /// only when every counted roster is placed in one, so one unplaced roster shows the season flat.
        /// A division whose every member is excluded (A1) is left out. Null when not grouped.
        /// </summary>
        public IReadOnlyList<Division>? ShownDivisions =>
            GroupedDivisions is { } divisions && UnplacedRosters.Count == 0
                ? [.. divisions.Where(d => Teams.Any(t => t.Division == d.Number))]
                : null;

        /// <summary>
        /// Gets the counted rosters with no division in range, in a head-to-head league that has divisions (T72b).
        /// </summary>
        public IReadOnlyList<int> UnplacedRosters =>
            GroupedDivisions is { } divisions
                ? [.. Teams.Where(t => t.Division is not { } n || n < 1 || n > divisions.Count).Select(t => t.RosterId)]
                : [];

        /// <summary>
        /// Gets the league's divisions where they can group the table: head-to-head only (Q14).
        /// </summary>
        private IReadOnlyList<Division>? GroupedDivisions =>
            Format == LeagueFormat.HeadToHead ? Divisions : null;

        /// <summary>
        /// Gets the first playoff week, or 0 without playoffs.
        /// </summary>
        public int PlayoffWeekStart => HasPlayoffs ? RegularSeasonCutoff + 1 : 0;

        /// <summary>
        /// Gets whether the title is the bracket's: head-to-head with playoffs (A46).
        /// </summary>
        public bool HasBracket => Format == LeagueFormat.HeadToHead && HasPlayoffs;

        /// <summary>
        /// Gets the last week whose results stand: a past season's are all settled, its last weeks stored or not.
        /// </summary>
        public int SettledThroughWeek => IsCurrent ? FinalThroughWeek : int.MaxValue;

        /// <summary>
        /// Gets the bracket's rounds in weeks (T69), or null when there is no bracket to show:
        /// not head-to-head with playoffs, not read, empty, or not placeable (A46).
        /// </summary>
        public IReadOnlyDictionary<int, IReadOnlyList<int>>? PlayoffRounds =>
            HasBracket && Bracket is { } bracket
                ? Playoffs.Weeks(bracket, PlayoffWeekStart, PlayoffRoundType)
                : null;

        /// <summary>
        /// Gets the final, the match deciding first place, when the bracket can be shown.
        /// </summary>
        public PlayoffMatch? Final => PlayoffRounds is not null && Bracket is { } bracket ? bracket.FirstOrDefault(m => m.Placement == 1) : null;

        /// <summary>
        /// Gets the week the season ends (A46): its final's last week in head-to-head with playoffs,
        /// week 18 when that bracket cannot be shown, else the last counted week.
        /// </summary>
        public int SeasonEndWeek =>
            !HasBracket ? LastCountedWeek
            : PlayoffRounds is { } rounds ? rounds.Values.Max(w => w[^1])
            : RegularSeason.NFLWeeks;

        /// <summary>
        /// Gets whether the season is over: what stops the page polling, says "Season completed"
        /// and crowns a champion (A45, A46). A past season always is.
        /// </summary>
        public bool IsComplete =>
            !IsCurrent
            || (Final is { } final && PlayoffRounds is { } rounds
                ? Playoffs.IsSettled(final, rounds[final.Round], FinalThroughWeek)
                : FinalThroughWeek >= SeasonEndWeek);

        /// <summary>
        /// Gets whether the league schedules playoffs (T70): if so its champion is the bracket's (T69),
        /// not the table's. A cut-off short of the NFL's 18 is exactly a playoff start of 2 to 18
        /// (<see cref="RegularSeason.LastWeek"/>), so it is read from the cut-off, never kept beside it.
        /// </summary>
        public bool HasPlayoffs => RegularSeasonCutoff < RegularSeason.NFLWeeks;

        /// <summary>
        /// Gets the last week that counts: the cut-off, less any settled weeks at its end nobody scored in (A37).
        /// </summary>
        /// <remarks>
        /// Trimmed on the counted rosters only, so the placeholder's scores cannot keep a week.
        /// A past season is settled to its end, even where its stored weeks stop short of the cut-off.
        /// </remarks>
        public int LastCountedWeek => RegularSeason.LastCountedWeek(
            RegularSeasonCutoff,
            IsCurrent ? FinalThroughWeek : int.MaxValue,
            Teams.SelectMany(t => t.Weeks));

        /// <summary>
        /// Gets the week the matchups panel shows (T71, A41): the current week, held to the last
        /// week that counts, so a playoff week or a past season shows its last regular week.
        /// Zero in the preseason, which has no games.
        /// </summary>
        public int MatchupWeek => Math.Min(CurrentWeek, LastCountedWeek);

        /// <summary>
        /// Gets every roster's head-to-head record, the placeholder's included, or null while a
        /// counted week lacks its games (T68). Built in both formats, so the check runs on every league.
        /// The placeholder's own record is here for the check; nothing may show it (A1).
        /// </summary>
        public IReadOnlyDictionary<int, TeamRecord>? Records =>
            HeadToHead.Records(AllWeeks, LastCountedWeek, HasMedianGame);

        /// <summary>
        /// Gets every settled week to store, the excluded rosters' included (T68).
        /// </summary>
        public IEnumerable<TeamWeek> FinalWeeks =>
            AllWeeks.Where(w => w.IsFinal);

        /// <summary>
        /// Gets every roster's weeks, the excluded rosters' included: only head-to-head and storage read these (A1).
        /// </summary>
        private IEnumerable<TeamWeek> AllWeeks => Teams.Concat(ExcludedTeams).SelectMany(t => t.Weeks);
    }
}
