namespace FantasyLeague.Core
{
    /// <summary>
    /// Where finalized weeks live once they can no longer change.
    /// </summary>
    /// <remarks>
    /// The port sits in <c>Core</c> so <c>Data</c> implements it against the
    /// domain alone and <c>Seasons</c> consumes it without an EF Core dependency.
    /// The console harness runs on <see cref="NullFinalizedWeekStore"/> with no
    /// connection string. The same seam as <see cref="IPlayerMapStore"/> (A21).
    /// </remarks>
    public interface IFinalizedWeekStore
    {
        /// <summary>
        /// Reads every stored week for one season.
        /// </summary>
        /// <param name="leagueId">The league the season belongs to.</param>
        /// <param name="season">The season year.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>
        /// Every finalized week held for that season, empty when none are stored.
        /// </returns>
        Task<IReadOnlyList<TeamWeek>> ReadAsync(
            string leagueId,
            int season,
            CancellationToken cancellationToken);

        /// <summary>
        /// Stores weeks that have finalized.
        /// </summary>
        /// <param name="leagueId">The league the season belongs to.</param>
        /// <param name="season">The season year.</param>
        /// <param name="weeks">The weeks to store. Callers pass only finalized weeks.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>A task that completes when the weeks have been stored.</returns>
        /// <remarks>
        /// Idempotent: the same week written twice upserts on (Season, LeagueId, RosterId, Week).
        /// </remarks>
        Task WriteAsync(
            string leagueId,
            int season,
            IReadOnlyList<TeamWeek> weeks,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// The store used when no database is configured.
    /// </summary>
    public sealed class NullFinalizedWeekStore : IFinalizedWeekStore
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<TeamWeek>> ReadAsync(
            string leagueId,
            int season,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TeamWeek>>([]);

        /// <inheritdoc />
        public Task WriteAsync(
            string leagueId,
            int season,
            IReadOnlyList<TeamWeek> weeks,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
