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
    /// <param name="RecordMismatches">Rosters whose record disagrees with Sleeper's (T68), reported like <see cref="Violations"/>.</param>
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
        IReadOnlyList<RecordMismatch> RecordMismatches)
    {
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
