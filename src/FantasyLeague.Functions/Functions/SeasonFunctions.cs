using System.Reflection;
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
        /// The commit this build was made from, sent as <c>X-Build</c> so the
        /// deploy can tell its own build from the one it replaced.
        /// </summary>
        private static readonly string Build =
            typeof(SeasonFunctions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+') is [_, var commit]
                ? commit
                : "local";

        /// <summary>
        /// Serves one season.
        /// </summary>
        /// <param name="req">The request. Only <c>season</c> is read.</param>
        /// <returns>The season payload, 400 for a malformed year, 404 for one with no data.</returns>
        [Function("GetSeason")]
        public async Task<IActionResult> GetSeason(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "season")] HttpRequest req,
            CancellationToken cancellationToken)
        {
            req.HttpContext.Response.Headers["X-Build"] = Build;

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
            var snapshot = requested is null || requested == current
                ? await seasons.BuildAsync(cancellationToken)
                : await seasons.BuildPastAsync(requested.Value, cancellationToken);

            // a year with no data is a well-formed request for something absent
            if (snapshot is null)
            {
                response.Headers.CacheControl = "no-store";
                var available = await seasons.GetAvailableSeasonsAsync(cancellationToken);

                return new NotFoundObjectResult(SeasonPayloadMapper.ToSeasonsPayload(current, available));
            }

            var payload = SeasonPayloadMapper.ToSeasonPayload(snapshot, time.GetUtcNow());
            var etag = SeasonETag.Compute(payload);

            response.Headers.CacheControl = SeasonETag.CacheControl(payload);
            response.Headers.ETag = etag;

            return SeasonETag.Matches(req.Headers.IfNoneMatch, etag)
                ? new StatusCodeResult(StatusCodes.Status304NotModified)
                : new OkObjectResult(payload);
        }

        /// <summary>
        /// Serves the seasons that can be asked for.
        /// </summary>
        /// <param name="req">The request. Nothing is read from it.</param>
        /// <returns>The current season and every season with data.</returns>
        [Function("GetSeasons")]
        public async Task<IActionResult> GetSeasons(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "seasons")] HttpRequest req,
            CancellationToken cancellationToken)
        {
            var current = await seasons.GetCurrentSeasonAsync(cancellationToken);
            var payload = SeasonPayloadMapper.ToSeasonsPayload(
                current, await seasons.GetAvailableSeasonsAsync(cancellationToken));
            var etag = SeasonETag.Compute(payload);

            // a new season appears once a year, so a day's delay at the rollover costs nothing
            req.HttpContext.Response.Headers.CacheControl = "public, max-age=86400";
            req.HttpContext.Response.Headers.ETag = etag;

            return SeasonETag.Matches(req.Headers.IfNoneMatch, etag)
                ? new StatusCodeResult(StatusCodes.Status304NotModified)
                : new OkObjectResult(payload);
        }
    }
}
