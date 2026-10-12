using FantasyLeague.Core;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// One league season as Sleeper describes it.
    /// </summary>
    /// <param name="Name">The league's name, or a default when the commissioner set none.</param>
    /// <param name="Slots">Startable slots in roster_positions order, for this season only (A3).</param>
    /// <param name="TeamNames">Display name per roster id.</param>
    /// <param name="RosterOwnerIds">Owning user id per roster id; unowned rosters are absent. What A1 matches on.</param>
    public sealed record SleeperLeague(
        string Name,
        IReadOnlyList<Slot> Slots,
        IReadOnlyDictionary<int, string> TeamNames,
        IReadOnlyDictionary<int, string> RosterOwnerIds);
}
