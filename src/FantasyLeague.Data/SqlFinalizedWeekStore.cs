using System.Text.Json;
using FantasyLeague.Core;
using FantasyLeague.Data.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Data
{
    internal sealed record StoredPlayer(
        string Id,
        string Name,
        IReadOnlyList<string> Positions,
        decimal Points);

    internal sealed class SqlFinalizedWeekStore(
        IDbContextFactory<FantasyDataContext> contexts,
        TimeProvider time,
        ILogger<SqlFinalizedWeekStore> logger) : IFinalizedWeekStore
    {
        private static readonly JsonSerializerOptions Json = new();

        /// <summary>
        /// Reads a given set of team weeks for a league and season
        /// </summary>
        /// <param name="leagueId">League Id</param>
        /// <param name="season">Season year</param>
        /// <param name="cancellationToken"></param>
        /// <returns>List of teamweeks for the year and league</returns>
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

        /// <summary>
        /// Writes a given set of weeks to the db
        /// </summary>
        /// <param name="leagueId">Leage to write to</param>
        /// <param name="season">Season to write to</param>
        /// <param name="weeks">Week data to write</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Nothing - await async op</returns>
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
        /// Creates a new TeamWeek object based on the input WeekScore
        /// </summary>
        /// <param name="row">WeekScore object to use</param>
        /// <returns>Teamweek object</returns>
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
        /// Creates a WeekScore db object based on an input teamweek
        /// </summary>
        /// <param name="leagueId">League to apply change to</param>
        /// <param name="season">Season to modify</param>
        /// <param name="week">Teamweek data object</param>
        /// <param name="now">When the row was computed</param>
        /// <returns>Weekscore object created</returns>
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
        /// Applies teamweek to the WeekScore
        /// </summary>
        /// <param name="week">TeamWeek source of data</param>
        /// <param name="row">WeekScore target for data</param>
        /// <param name="now">When the row was computed</param>
        private static void Apply(TeamWeek week, WeekScore row, DateTimeOffset now)
        {
            row.ActualPts = week.Actual;
            row.IdealPts = week.Ideal;
            row.StartersJson = ToJson(week.Starters);
            row.IdealLineupJson = ToJson(week.IdealLineup);
            row.PlayersPointsJson = ToPlayersJson(week.Players);
            row.SlotAveragesJson = JsonSerializer.Serialize(week.SlotAverages, Json);
            row.IsFinal = week.IsFinal;
            row.ComputedUtc = now;
        }

        /// <summary>
        /// Tranlates player objects to Json
        /// </summary>
        /// <param name="players">Plays to Json-ize</param>
        /// <returns>Json string</returns>
        private static string ToJson(IReadOnlyList<PlayerId> players) => JsonSerializer.Serialize(players.Select(p => p.Value).ToArray(), Json);

        /// <summary>
        /// Writes a list of players to Json
        /// </summary>
        /// <param name="players">List of players to Json-ize</param>
        /// <returns>JSON-ized string of players</returns>
        private static string ToPlayersJson(IReadOnlyList<RosteredPlayer> players) =>
            JsonSerializer.Serialize(
                players.Select(p => new StoredPlayer(p.Id.Value, p.Name, p.FantasyPositions, p.Points)).ToArray(),
                Json);

        /// <summary>
        /// Gets rosterd players from Json
        /// </summary>
        /// <param name="json">Json player string</param>
        /// <returns>Rostered players</returns>
        private static IReadOnlyList<RosteredPlayer> FromPlayersJson(string json) => string.IsNullOrWhiteSpace(json)
                ? [] : [.. (JsonSerializer.Deserialize<StoredPlayer[]>(json, Json) ?? [])
                    .Select(p => new RosteredPlayer(new PlayerId(p.Id), p.Name, p.Positions, p.Points))];

        /// <summary>
        /// Gets plays Ids from Json
        /// </summary>
        /// <param name="json">String player json</param>
        /// <returns>List of player Ids</returns>
        private static IReadOnlyList<PlayerId> FromJson(string json) => string.IsNullOrWhiteSpace(json) ? []
                : [.. (JsonSerializer.Deserialize<string[]>(json, Json) ?? []).Select(v => new PlayerId(v))];

        /// <summary>
        /// Reads per-slot averages from Json
        /// </summary>
        /// <param name="json">Json slot-average string</param>
        /// <returns>Per-slot averages, or empty when the row predates M6 or the week carried none</returns>
        private static IReadOnlyDictionary<string, decimal> FromSlotJson(string json) =>
            string.IsNullOrWhiteSpace(json)
                ? new Dictionary<string, decimal>()
                : JsonSerializer.Deserialize<Dictionary<string, decimal>>(json, Json) ?? new Dictionary<string, decimal>();
    }
}
