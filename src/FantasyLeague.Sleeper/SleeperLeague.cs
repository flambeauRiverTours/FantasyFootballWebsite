using FantasyLeague.Core;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One league season as Sleeper describes it.
    /// </summary>
    /// <param name="Name">The league's name, or a default when the commissioner set none.</param>
    /// <param name="Slots">Startable slots in roster_positions order, for this season only (A3).</param>
    /// <param name="Teams">Every roster, by roster id.</param>
    /// <param name="PlayoffWeekStart">The first playoff week as Sleeper sends it: zero or null when the league has none. Weeks from here on are not counted (A37).</param>
    /// <param name="HasMedianGame">Whether each week adds a result against the league's median score (Q12, T68).</param>
    public sealed record SleeperLeague(
        string Name,
        IReadOnlyList<Slot> Slots,
        IReadOnlyDictionary<int, SleeperTeam> Teams,
        int? PlayoffWeekStart,
        bool HasMedianGame);

    /// <summary>
    /// One roster as the league shows it.
    /// </summary>
    /// <param name="Name">The name to show it under, never blank.</param>
    /// <param name="OwnerId">The owning user's id, or null for an orphaned roster. What A1 matches on.</param>
    /// <param name="Record">Sleeper's own record so far, or null when it sent none. Checked against ours (T68).</param>
    public sealed record SleeperTeam(string Name, string? OwnerId, TeamRecord? Record)
    {
        /// <summary>
        /// The name shown for a roster with no manager or no name of its own.
        /// </summary>
        /// <param name="rosterId">The roster.</param>
        /// <returns>For example, <c>Roster 3</c>.</returns>
        public static string FallbackName(int rosterId) => $"Roster {rosterId}";
    }
}
