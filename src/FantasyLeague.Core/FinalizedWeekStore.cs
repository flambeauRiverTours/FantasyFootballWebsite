namespace FantasyLeague.Core
{
    /// <summary>
    /// Where finalized weeks live once they can no longer change.
    /// </summary>
    /// <remarks>
    /// The port sits in <c>Core</c> rather than <c>Sleeper</c> so the SQL adapter
    /// can be implemented against the domain alone. <c>Sleeper</c> consumes this
    /// without gaining an EF Core dependency, and the console harness keeps
    /// running on <see cref="NullFinalizedWeekStore"/> with no connection string.
    /// The same seam as the player map store (A12): an interface the domain owns,
    /// a null implementation for hosts with no database, and a real one wired in
    /// by whichever host has the connection.
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
        /// Every finalized week held for that season, empty when none are stored
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
        /// Idempotent: the same week written twice upserts on (Season, LeagueId, RosterId, Week)
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
