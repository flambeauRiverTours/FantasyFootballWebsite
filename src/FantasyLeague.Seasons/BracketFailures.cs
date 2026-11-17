using System.Collections.Concurrent;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// When each league's bracket read last failed (A46). A singleton: the season service is
    /// created per request, so a hold kept on it would be forgotten by the next one.
    /// </summary>
    /// <param name="time">The clock the hold is measured on.</param>
    internal sealed class BracketFailures(TimeProvider time)
    {
        private static readonly TimeSpan Hold = TimeSpan.FromMinutes(1);

        private readonly ConcurrentDictionary<string, DateTimeOffset> until = new();

        /// <summary>
        /// Gets whether a read of this league's bracket failed within the hold.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <returns>True while the failure is held.</returns>
        public bool IsHeld(string leagueId) => until.TryGetValue(leagueId, out var end) && end > time.GetUtcNow();

        /// <summary>
        /// Records a failed read, held for a minute.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        public void Record(string leagueId) => until[leagueId] = time.GetUtcNow() + Hold;
    }
}
