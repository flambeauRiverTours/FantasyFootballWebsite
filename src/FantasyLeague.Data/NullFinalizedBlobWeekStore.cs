using FantasyLeague.Core;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Nullified implementation of IBlobFinalizedWeekStore used for unit testing
    /// </summary>
    internal class NullBlobFinalizedWeekStore : IBlobFinalizedWeekStore
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<TeamWeek>?> ReadAsync(string leagueId, int season, CancellationToken ct) => Task.FromResult<IReadOnlyList<TeamWeek>?>(null);
        /// <inheritdoc />
        public Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken ct) => Task.CompletedTask;
    }
}
