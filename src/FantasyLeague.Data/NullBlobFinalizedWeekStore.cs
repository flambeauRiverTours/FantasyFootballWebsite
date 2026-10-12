using FantasyLeague.Core;

namespace FantasyLeague.Data
{
    /// <summary>
    /// The blob cache used when no storage account is configured: always a miss.
    /// </summary>
    internal sealed class NullBlobFinalizedWeekStore : IBlobFinalizedWeekStore
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<TeamWeek>?> ReadAsync(string leagueId, int season, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TeamWeek>?>(null);

        /// <inheritdoc />
        public Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
