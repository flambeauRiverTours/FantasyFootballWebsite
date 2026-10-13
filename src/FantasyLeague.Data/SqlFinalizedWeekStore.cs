using System.Text.Json;
using FantasyLeague.Core;
using FantasyLeague.Data.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Every season's finalized weeks, in SQL (A20).
    /// </summary>
    /// <param name="contexts">Creates a context per call.</param>
    /// <param name="time">Clock for each row's write time.</param>
    /// <param name="logger">Logger.</param>
    internal sealed class SqlFinalizedWeekStore(
        IDbContextFactory<FantasyDataContext> contexts,
        TimeProvider time,
        ILogger<SqlFinalizedWeekStore> logger) : IFinalizedWeekStore
    {
        private static readonly JsonSerializerOptions Json = new();

        /// <inheritdoc />
        /// <remarks>
        /// A database still waking after its retries throws.
        /// <see cref="FinalizedWeekStore"/> turns that into a rebuild from
        /// Sleeper, where the failure is kept out of the cache.
        /// </remarks>
        public async Task<IReadOnlyList<TeamWeek>> ReadAsync(string leagueId, int season, CancellationToken cancellationToken)
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            var rows = await db.WeekScores.Where(w => w.LeagueId == leagueId && w.Season == season && w.IsFinal)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            logger.LogInformation("Read {RowCount} stored weeks for season {Season}.", rows.Count, season);

            return [.. rows.Select(ToDomain)];
        }

        /// <inheritdoc />
        public async Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken cancellationToken)
        {
            // a stored week is served as settled, so a live one would freeze mid-game
            if (weeks.FirstOrDefault(w => !w.IsFinal) is { } live)
            {
                throw new ArgumentException(
                    $"Only finalized weeks are stored; week {live.Week} of roster {live.RosterId} is still in progress.",
                    nameof(weeks));
            }

            if (weeks.Count == 0)
            {
                return;
            }

            await using var db = await contexts.CreateDbContextAsync(cancellationToken);

            var weekNumbers = weeks.Select(w => w.Week).Distinct().ToList();

            var existing = await db.WeekScores.Where(w => w.LeagueId == leagueId && w.Season == season && weekNumbers.Contains(w.Week))
                .ToDictionaryAsync(w => (w.RosterId, w.Week), cancellationToken);

            var now = time.GetUtcNow();
            var added = 0;
            var updated = 0;

            foreach (var week in weeks)
            {
                if (existing.TryGetValue((week.RosterId, week.Week), out var row))
                {
                    Apply(week, row, now);
                    updated++;
                }
                else
                {
                    db.WeekScores.Add(ToRow(leagueId, season, week, now));
                    added++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Stored season {Season}: {Added} added, {Updated} updated.", season, added, updated);
        }

        /// <summary>
        /// Reads a row back into the domain.
        /// </summary>
        /// <param name="row">The stored row.</param>
        /// <returns>The week it holds.</returns>
        private static TeamWeek ToDomain(WeekScore row) =>
            new(row.RosterId,
                row.Week,
                row.ActualPts,
                row.IdealPts,
                FromJson<IReadOnlyList<PlayerId>>(row.StartersJson),
                FromJson<IReadOnlyList<PlayerId>>(row.IdealLineupJson),
                FromJson<IReadOnlyList<RosteredPlayer>>(row.PlayersJson),
                row.IsFinal,

                // the one column with an empty past: rows written before AddSlotAveragesJson
                row.SlotAveragesJson.Length == 0
                    ? new Dictionary<string, decimal>()
                    : FromJson<IReadOnlyDictionary<string, decimal>>(row.SlotAveragesJson));

        /// <summary>
        /// Creates the row for a week not yet stored.
        /// </summary>
        /// <param name="leagueId">The league.</param>
        /// <param name="season">The season year.</param>
        /// <param name="week">The week to store.</param>
        /// <param name="now">When the row is written.</param>
        /// <returns>The new row.</returns>
        private static WeekScore ToRow(string leagueId, int season, TeamWeek week, DateTimeOffset now)
        {
            var row = new WeekScore
            {
                LeagueId = leagueId,
                Season = season,
                Week = week.Week,
                RosterId = week.RosterId,
            };

            Apply(week, row, now);
            return row;
        }

        /// <summary>
        /// Copies a week's values onto its row, new or existing.
        /// </summary>
        /// <param name="week">The week to store.</param>
        /// <param name="row">The row to update.</param>
        /// <param name="now">When the row is written.</param>
        private static void Apply(TeamWeek week, WeekScore row, DateTimeOffset now)
        {
            row.ActualPts = week.ActualPts;
            row.IdealPts = week.IdealPts;
            row.StartersJson = ToJson(week.Starters);
            row.IdealLineupJson = ToJson(week.IdealLineup);
            row.PlayersJson = ToJson(week.Players);
            row.SlotAveragesJson = ToJson(week.SlotAverages);
            row.IsFinal = week.IsFinal;
            row.ComputedUtc = now;
        }

        /// <summary>
        /// Writes one column's value as JSON, in the shape the blob stores it too.
        /// </summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>The JSON.</returns>
        private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Json);

        /// <summary>
        /// Reads one column's JSON back.
        /// </summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="json">The column.</param>
        /// <returns>The value.</returns>
        /// <exception cref="JsonException">The column is empty or not valid JSON: a row this store never wrote.</exception>
        /// <exception cref="InvalidDataException">The column holds JSON <c>null</c>.</exception>
        private static T FromJson<T>(string json)
            where T : class =>
            JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidDataException("A stored week's column held JSON null.");
    }
}
