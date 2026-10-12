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
    /// <param name="time">Clock for the response timestamp.</param>
    /// <param name="logger">Logger.</param>
    public sealed class SeasonFunctions(
        ISeasonService seasons,
        IOptions<LeagueOptions> league,
        TimeProvider time,
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

            var response = req.HttpContext.Response;
            var current = await seasons.GetCurrentSeasonAsync(ct);

            // a year with no data is a well-formed request for something absent
            if (requested != null && requested != current)
            {
                response.Headers.CacheControl = "no-store";

                return new NotFoundObjectResult(SeasonPayloadMapper.ToSeasonsPayload(current));
            }

            var snapshot = await seasons.BuildAsync(league.Value.CurrentLeagueId, ct);
            var etag = SeasonPayloadMapper.ComputeETag(snapshot);

            response.Headers.CacheControl = "public, max-age=5";
            response.Headers.ETag = etag;

            if (SeasonPayloadMapper.MatchesETag(req.Headers.IfNoneMatch, etag))
            {
                return new StatusCodeResult(StatusCodes.Status304NotModified);
            }

            return new OkObjectResult(SeasonPayloadMapper.ToIndividualSeasonPayload(
                snapshot, time.GetUtcNow(), ExcludedRosterIds(snapshot), league.Value.OverrideLeagueName));
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
            var current = await seasons.GetCurrentSeasonAsync(ct);

            req.HttpContext.Response.Headers.CacheControl = "public, max-age=300";

            return new OkObjectResult(SeasonPayloadMapper.ToSeasonsPayload(current));
        }

        /// <summary>
        /// Resolves the configured excluded owners to this season's roster ids.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <returns>Roster ids to leave out of every metric.</returns>
        private IReadOnlySet<int> ExcludedRosterIds(SeasonSnapshot snapshot)
        {
            var excluded = SeasonPayloadMapper.ResolveExcludedRosters(snapshot, league.Value.ExcludedOwnerIds);

            foreach (var ownerId in excluded.UnmatchedOwnerIds)
            {
                logger.LogWarning("Excluded owner {OwnerId} matches no roster in season {Season}; that team is still counted in every metric.", ownerId, snapshot.Season);
            }

            return excluded.RosterIds;
        }
    }
}
