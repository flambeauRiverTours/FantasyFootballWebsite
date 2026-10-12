using FantasyLeague.Core;

namespace FantasyLeague.Data
{
    /// <summary>
    /// The current season's finalized weeks, cached in blob storage (A20).
    /// </summary>
    internal interface IBlobFinalizedWeekStore
    {
        /// <summary>
        /// Reads the cached season.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="season">The season wanted.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The weeks, or null when the blob holds another season, nothing, or cannot be reached.</returns>
        Task<IReadOnlyList<TeamWeek>?> ReadAsync(string leagueId, int season, CancellationToken cancellationToken);

        /// <summary>
        /// Replaces the cached season, unless the blob already holds a newer one.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="season">The season the weeks belong to.</param>
        /// <param name="weeks">The season's finalized weeks.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>A task that completes when the write has been attempted. Failures are logged, never thrown.</returns>
        Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken cancellationToken);
    }
}
