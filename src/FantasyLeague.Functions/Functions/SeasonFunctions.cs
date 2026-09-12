using FantasyLeague.Sleeper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Functions.Functions
{
    /// <summary>
    /// The two read-only endpoints the site is built on.
    /// </summary>
    /// <param name="seasons">Season assembly.</param>
    /// <param name="league">The configured league. Never taken from the request.</param>
    /// <param name="logger">Logger.</param>
    public sealed class SeasonFunctions(
        ISeasonService seasons,
        IOptions<LeagueOptions> league,
        ILogger<SeasonFunctions> logger)
    {
        /// <summary>
        /// Serves one season.
        /// </summary>
        /// <param name="req">The request. Only <c>season</c> is read.</param>
        /// <param name="ct">CancellationToken.</param>
        /// <returns>The season payload, 400 for a malformed year, 404 for one with no data.</returns>
        [Function("GetSeason")]
        public async Task<IActionResult> GetSeason(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "season")] HttpRequest req,
            CancellationToken ct)
        {
            var raw = req.Query["season"].ToString();
            int? requested = null;

            if (!string.IsNullOrWhiteSpace(raw))
            {
                if (!int.TryParse(raw, out var parsed))
                {
                    return new BadRequestObjectResult(new { error = $"'{raw}' is not a season." });
                }

                requested = parsed;
            }

            var snapshot = await seasons.BuildAsync(league.Value.CurrentLeagueId, ct);

            LogViolations(snapshot);
            LogUnresolvedPlayerIds(snapshot);

            // a year with no data is a well-formed request for something absent
            if (requested is not null && requested != snapshot.Season)
            {
                return new NotFoundObjectResult(
                    SeasonPayloadMapper.ToSeasonsPayload(snapshot.Season));
            }

            return new OkObjectResult(SeasonPayloadMapper.ToIndividualSeasonPayload(
                snapshot, DateTimeOffset.UtcNow, requested, ExcludedRosterIds(snapshot)));
        }

        /// <summary>
        /// Serves the seasons that can be asked for.
        /// </summary>
        /// <param name="req">The request. Nothing is read from it.</param>
        /// <param name="ct">CancellationToken.</param>
        /// <returns>The current season and every season with data.</returns>
        [Function("GetSeasons")]
        public async Task<IActionResult> GetSeasons(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "seasons")] HttpRequest req,
            CancellationToken ct)
        {
            var snapshot = await seasons.BuildAsync(league.Value.CurrentLeagueId, ct);

            return new OkObjectResult(SeasonPayloadMapper.ToSeasonsPayload(snapshot.Season));
        }

        /// <summary>
        /// Resolves the configured excluded owners to this season's roster ids.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <returns>Roster ids to leave out of every metric.</returns>
        /// <remarks>
        /// A1 matches on user id, not team name: a rename would otherwise put the
        /// placeholder team back into the standings and every league-wide average.
        /// Roster id is only used within this one season, never persisted as identity.
        /// </remarks>
        private IReadOnlySet<int> ExcludedRosterIds(SeasonSnapshot snapshot)
        {
            var owners = league.Value.ExcludedOwnerIds;
            if (owners.Count == 0)
            {
                return new HashSet<int>();
            }

            return snapshot.RosterOwnerIds
                .Where(pair => owners.Contains(pair.Value, StringComparer.Ordinal))
                .Select(pair => pair.Key)
                .ToHashSet();
        }

        /// <summary>
        /// Records every invariant breach without failing the request.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        private void LogViolations(SeasonSnapshot snapshot)
        {
            foreach (var violation in snapshot.Violations)
            {
                logger.LogWarning(
                    "Invariant {Invariant} failed for roster {RosterId} in week {Week}: expected {Expected}, got {Actual}.",
                    violation.Invariant, violation.RosterId, violation.Week, violation.Expected, violation.Actual);
            }
        }

        /// <summary>
        /// Records every rostered player the map could not explain.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        private void LogUnresolvedPlayerIds(SeasonSnapshot snapshot)
        {
            foreach (var playerId in snapshot.UnresolvedPlayerIds)
            {
                logger.LogWarning("Rostered player {PlayerId} is missing from the player map.", playerId);
            }
        }
    }
}
