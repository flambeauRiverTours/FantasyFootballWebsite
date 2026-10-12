using FantasyLeague.Core;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// The player dump, projected down to the slim map.
    /// </summary>
    /// <param name="FetchedCount">How many players the dump held. A dump that quietly shrinks shows up here.</param>
    /// <param name="Players">Every startable player by id.</param>
    public sealed record PlayerDump(
        int FetchedCount,
        IReadOnlyDictionary<string, PlayerRef> Players);
}
