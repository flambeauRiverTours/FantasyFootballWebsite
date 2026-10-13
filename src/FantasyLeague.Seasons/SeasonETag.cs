using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// The season response's entity tag, and whether a conditional request already holds it.
    /// </summary>
    /// <remarks>
    /// Kept beside the payload rather than in the Functions host so it can be
    /// tested without one.
    /// </remarks>
    public static class SeasonETag
    {
        /// <summary>
        /// Builds a validator for one response.
        /// </summary>
        /// <param name="payload">The response body.</param>
        /// <returns>A weak entity tag.</returns>
        /// <remarks>
        /// A hash of the body itself, minus <c>asOfUtc</c>, so anything the page can
        /// see changes the tag and there is no list of fields to keep in step.
        /// Weak because <c>asOfUtc</c> still differs on every response; a strong
        /// tag would promise byte-identical content it cannot deliver.
        /// </remarks>
        public static string Compute(SeasonPayload payload)
        {
            var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload with { AsOfUtc = default }));

            // 16 hex characters is plenty for a weak tag
            return $"W/\"{payload.Season}-wk{payload.CurrentWeek}-{Convert.ToHexString(hash)[..16]}\"";
        }

        /// <summary>
        /// Decides whether a conditional request already holds this version.
        /// </summary>
        /// <param name="ifNoneMatch">The request's If-None-Match header.</param>
        /// <param name="etag">The tag from <see cref="Compute"/>.</param>
        /// <returns><c>true</c> when a 304 is the right answer.</returns>
        /// <remarks>
        /// The header is a comma separated list and a client may send the tag
        /// without its W/ prefix, so neither a string comparison nor a Contains
        /// call is enough.
        /// </remarks>
        public static bool Matches(StringValues ifNoneMatch, string etag)
        {
            if (StringValues.IsNullOrEmpty(ifNoneMatch))
            {
                return false;
            }

            if (!EntityTagHeaderValue.TryParse(etag, out var current))
            {
                return false;
            }

            if (!EntityTagHeaderValue.TryParseList([.. ifNoneMatch.OfType<string>()], out var candidates))
            {
                return false;
            }

            return candidates.Any(candidate => candidate.Compare(current, useStrongComparison: false));
        }
    }
}
