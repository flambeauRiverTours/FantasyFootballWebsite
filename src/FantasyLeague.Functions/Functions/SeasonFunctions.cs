using FantasyLeague.Seasons;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace FantasyLeague.Functions.Functions
{
    /// <summary>
    /// The two read-only endpoints the site is built on.
    /// </summary>
    /// <remarks>
    /// The league is configuration, never a request parameter: a league id in a
    /// query string would make this an open relay against Sleeper's rate limit.
    /// </remarks>
    /// <param name="seasons">Season assembly for the configured league.</param>
    /// <param name="time">Clock for the response timestamp.</param>
    public sealed class SeasonFunctions(ISeasonService seasons, TimeProvider time)
    {
        /// <summary>
        /// Serves one season.
        /// </summary>
        /// <param name="req">The request. Only <c>season</c> is read.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The season payload, 400 for a malformed year, 404 for one with no data.</returns>
        [Function("GetSeason")]
        public async Task<IActionResult> GetSeason(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "season")] HttpRequest req,
            CancellationToken cancellationToken)
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
            var current = await seasons.GetCurrentSeasonAsync(cancellationToken);

            // a year with no data is a well-formed request for something absent
            if (requested != null && requested != current)
            {
                response.Headers.CacheControl = "no-store";

                return new NotFoundObjectResult(SeasonPayloadMapper.ToSeasonsPayload(current));
            }

            var snapshot = await seasons.BuildAsync(cancellationToken);
            var payload = SeasonPayloadMapper.ToSeasonPayload(snapshot, time.GetUtcNow());
            var etag = SeasonPayloadMapper.ComputeETag(payload);

            response.Headers.CacheControl = "public, max-age=5";
            response.Headers.ETag = etag;

            return SeasonPayloadMapper.MatchesETag(req.Headers.IfNoneMatch, etag)
                ? new StatusCodeResult(StatusCodes.Status304NotModified)
                : new OkObjectResult(payload);
        }

        /// <summary>
        /// Serves the seasons that can be asked for.
        /// </summary>
        /// <param name="req">The request. Nothing is read from it.</param>
        /// <param name="cancellationToken">CancellationToken.</param>
        /// <returns>The current season and every season with data.</returns>
        [Function("GetSeasons")]
        public async Task<IActionResult> GetSeasons(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "seasons")] HttpRequest req,
            CancellationToken cancellationToken)
        {
            var current = await seasons.GetCurrentSeasonAsync(cancellationToken);

            req.HttpContext.Response.Headers.CacheControl = "public, max-age=300";

            return new OkObjectResult(SeasonPayloadMapper.ToSeasonsPayload(current));
        }
    }
}
