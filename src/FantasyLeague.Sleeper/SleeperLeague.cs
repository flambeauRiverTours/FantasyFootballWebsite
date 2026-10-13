using FantasyLeague.Core;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One league season as Sleeper describes it.
    /// </summary>
    /// <param name="Name">The league's name, or a default when the commissioner set none.</param>
    /// <param name="Slots">Startable slots in roster_positions order, for this season only (A3).</param>
    /// <param name="Teams">Every roster, by roster id.</param>
    public sealed record SleeperLeague(
        string Name,
        IReadOnlyList<Slot> Slots,
        IReadOnlyDictionary<int, SleeperTeam> Teams);

    /// <summary>
    /// One roster as the league shows it.
    /// </summary>
    /// <param name="Name">The name to show it under, never blank.</param>
    /// <param name="OwnerId">The owning user's id, or null for an orphaned roster. What A1 matches on.</param>
    public sealed record SleeperTeam(string Name, string? OwnerId)
    {
        /// <summary>
        /// The name shown for a roster with no manager or no name of its own.
        /// </summary>
        /// <param name="rosterId">The roster.</param>
        /// <returns>For example, <c>Roster 3</c>.</returns>
        public static string FallbackName(int rosterId) => $"Roster {rosterId}";
    }
}
