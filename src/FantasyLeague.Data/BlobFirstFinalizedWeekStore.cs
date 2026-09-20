using System;
using System.Collections.Generic;
using System.Text;
using FantasyLeague.Core;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Week store implementated as a blob cache handling the current season in front of a
    /// sql store with all league data
    /// </summary>
    /// <param name="sql">Sql store with all data</param>
    /// <param name="blob">Blob cache with the current season's data</param>
    /// <param name="logger">Logger to log with</param>
    /// <param name="time">TimeProvider used for time operations</param>
    public sealed class BlobFirstFinalizedWeekStore(
    SqlFinalizedWeekStore sql,
    IBlobFinalizedWeekStore blob,
    ILogger<BlobFirstFinalizedWeekStore> logger,
    TimeProvider time) : IFinalizedWeekStore
    {
        public async Task<IReadOnlyList<TeamWeek>> ReadAsync(string leagueId, int season, CancellationToken ct) =>
            await blob.ReadAsync(leagueId, season, ct)
            ?? await sql.ReadAsync(leagueId, season, ct);

        public async Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken ct)
        {
            await sql.WriteAsync(leagueId, season, weeks, ct);   // SQL stays the durable copy
            if (SanityCheckSeasonForBlob(season))
            {
                await blob.WriteAsync(leagueId, season, weeks, ct);  // blob stays the fast one
            }
            else
            {
                logger.LogInformation("Skipped writing to blob current season cache. IFinalizedWeekStore::WriteAsync called for {season}", season);
            }
        }

        /// <summary>
        /// Determines whether the season is one we should write to the current season blob cache
        /// Prevents the overwriting in case of a backfill
        /// </summary>
        /// <param name="season">Int season (year)</param>
        /// <returns>True if the season is the current season that should be written to the blob, otherwise false</returns>
        private bool SanityCheckSeasonForBlob(int season)
        {
            DateTimeOffset seasonStart = new DateTimeOffset(season, 9, 1, 0, 0, 0, TimeSpan.Zero); //season start with some padding
            DateTimeOffset seasonEnd = new DateTimeOffset(season + 1, 3, 1, 0, 0, 0, TimeSpan.Zero); //season end, with some padding
            var now = time.GetUtcNow(); 
            if(DateTimeOffset.Compare(seasonStart, now) > 0 || DateTimeOffset.Compare(now, seasonEnd) > 0)
            {
                return false;
            }
            return true;
        }
    }
}
