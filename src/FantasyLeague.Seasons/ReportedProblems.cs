using System.Collections.Concurrent;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// The problems this instance has already logged.
    /// </summary>
    /// <remarks>
    /// A season is rebuilt every 5 s under load, and an unknown player or a
    /// broken invariant is still there on the next build. Logging it once per
    /// instance keeps one problem to one line rather than one every 5 s. A
    /// problem that outlives the instance is logged again by the next one,
    /// which on a plan that scales to zero is often enough to be seen.
    /// </remarks>
    internal sealed class ReportedProblems
    {
        private readonly ConcurrentDictionary<string, byte> seen = new(StringComparer.Ordinal);

        /// <summary>
        /// Records a problem, and says whether this is the first time.
        /// </summary>
        /// <param name="key">What identifies the problem.</param>
        /// <returns><c>true</c> the first time a key is seen.</returns>
        public bool IsNew(string key) => seen.TryAdd(key, 0);
    }
}
