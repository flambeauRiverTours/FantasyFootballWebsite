using System.Text.Json;
using FantasyLeague.Core;
using FantasyLeague.Data.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Data
{
    /// <summary>
    /// One rostered player inside <see cref="WeekScore.PlayersPointsJson"/>.
    /// </summary>
    /// <param name="Id">Sleeper's player id.</param>
    /// <param name="Name">The player's name.</param>
    /// <param name="Positions">The player's fantasy positions.</param>
    /// <param name="Points">What the player scored that week.</param>
    internal sealed record StoredPlayer(
        string Id,
        string Name,
        IReadOnlyList<string> Positions,
        decimal Points);

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
        /// A database still waking after its retries returns empty, so the
        /// season is rebuilt from Sleeper rather than the request failing.
        /// </remarks>
        public async Task<IReadOnlyList<TeamWeek>> ReadAsync(string leagueId, int season, CancellationToken cancellationToken)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var rows = await db.WeekScores.Where(w => w.LeagueId == leagueId && w.Season == season && w.IsFinal)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                logger.LogInformation("Read {RowCount} stored weeks for season {Season}.", rows.Count, season);

                return [.. rows.Select(ToDomain)];
            }
            catch (Exception ex) when (ex is RetryLimitExceededException or TimeoutException)
            {
                logger.LogWarning(ex, "Could not read stored weeks for season {Season}; rebuilding from Sleeper.", season);
                return [];
            }
        }

        /// <inheritdoc />
        public async Task WriteAsync(string leagueId, int season, IReadOnlyList<TeamWeek> weeks, CancellationToken cancellationToken)
        {
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
                FromJson(row.StartersJson),
                FromJson(row.IdealLineupJson),
                FromPlayersJson(row.PlayersPointsJson),
                row.IsFinal,
                FromSlotJson(row.SlotAveragesJson));

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
            row.PlayersPointsJson = ToPlayersJson(week.Players);
            row.SlotAveragesJson = JsonSerializer.Serialize(week.SlotAverages, Json);
            row.IsFinal = week.IsFinal;
            row.ComputedUtc = now;
        }

        /// <summary>
        /// Writes player ids as a JSON array.
        /// </summary>
        /// <param name="players">The ids.</param>
        /// <returns>The JSON.</returns>
        private static string ToJson(IReadOnlyList<PlayerId> players) =>
            JsonSerializer.Serialize(players.Select(p => p.Value).ToArray(), Json);

        /// <summary>
        /// Writes rostered players and their points as JSON.
        /// </summary>
        /// <param name="players">The players.</param>
        /// <returns>The JSON.</returns>
        private static string ToPlayersJson(IReadOnlyList<RosteredPlayer> players) =>
            JsonSerializer.Serialize(
                players.Select(p => new StoredPlayer(p.Id.Value, p.Name, p.FantasyPositions, p.Points)).ToArray(),
                Json);

        /// <summary>
        /// Reads rostered players and their points from JSON.
        /// </summary>
        /// <param name="json">The stored JSON.</param>
        /// <returns>The players.</returns>
        private static IReadOnlyList<RosteredPlayer> FromPlayersJson(string json) =>
            string.IsNullOrWhiteSpace(json)
                ? []
                : [.. (JsonSerializer.Deserialize<StoredPlayer[]>(json, Json) ?? [])
                    .Select(p => new RosteredPlayer(new PlayerId(p.Id), p.Name, p.Positions, p.Points))];

        /// <summary>
        /// Reads player ids from a JSON array.
        /// </summary>
        /// <param name="json">The stored JSON.</param>
        /// <returns>The ids.</returns>
        private static IReadOnlyList<PlayerId> FromJson(string json) =>
            string.IsNullOrWhiteSpace(json)
                ? []
                : [.. (JsonSerializer.Deserialize<string[]>(json, Json) ?? []).Select(v => new PlayerId(v))];

        /// <summary>
        /// Reads per-slot averages from JSON.
        /// </summary>
        /// <param name="json">The stored JSON.</param>
        /// <returns>Per-slot averages, empty for a row written before they existed or a week excluded from the radar.</returns>
        private static IReadOnlyDictionary<string, decimal> FromSlotJson(string json) =>
            string.IsNullOrWhiteSpace(json)
                ? new Dictionary<string, decimal>()
                : JsonSerializer.Deserialize<Dictionary<string, decimal>>(json, Json) ?? new Dictionary<string, decimal>();
    }
}
