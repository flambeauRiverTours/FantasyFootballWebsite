using FantasyLeague.Core;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Interface for the finalized week blob cache
    /// </summary>
    public interface IBlobFinalizedWeekStore
    {
        /// <summary>
        /// Reads data from the blob cache
        /// </summary>
        /// <param name="leagueId">League ID requested</param>
        /// <param name="season">Season year requested</param>
        /// <param name="ct">Cancelleation token</param>
        /// <returns>List of TeamWeeks if a matching cache was found, otherwise null</returns>
        public Task<IReadOnlyList<TeamWeek>?> ReadAsync(string leagueId, int season, CancellationToken ct);
        /// <summary>
        /// Writes data to the cache
        /// </summary>
        /// <param name="leagueId">League ID to write for</param>
        /// <param name="season">Season year to write for</param>
        /// <param name="weeks">Data to write to the cache as TeamWeek objects</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Task completion</returns>
        public Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken ct);
    }
}
