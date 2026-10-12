using FantasyLeague.Core;

namespace FantasyLeague.Data
{
    /// <summary>
    /// SQL as the record of every season, with the current season cached in a
    /// blob in front of it (A16, A20).
    /// </summary>
    /// <param name="sql">Every season's finalized weeks.</param>
    /// <param name="blob">The current season only. It decides for itself which season that is.</param>
    internal sealed class BlobFirstFinalizedWeekStore(
        SqlFinalizedWeekStore sql,
        IBlobFinalizedWeekStore blob) : IFinalizedWeekStore
    {
        /// <inheritdoc />
        public async Task<IReadOnlyList<TeamWeek>> ReadAsync(string leagueId, int season, CancellationToken ct)
        {
            var cached = await blob.ReadAsync(leagueId, season, ct);
            if (cached is not null)
            {
                return cached;
            }

            var fromSql = await sql.ReadAsync(leagueId, season, ct);

            // primes a cold blob after a deploy rather than waiting for Tuesday
            await blob.WriteAsync(leagueId, season, fromSql, ct);

            return fromSql;
        }

        /// <inheritdoc />
        public async Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken ct)
        {
            await sql.WriteAsync(leagueId, season, weeks, ct);
            await blob.WriteAsync(leagueId, season, weeks, ct);
        }
    }
}
