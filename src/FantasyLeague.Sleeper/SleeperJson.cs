using System.Text.Json;
using System.Text.Json.Serialization;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// The serializer settings every Sleeper payload is read with. Shared rather
    /// than per-DTO, because a difference between two of them would show up as a
    /// silently defaulted property rather than an error.
    /// </summary>
    internal static class SleeperJson
    {
        /// <summary>
        /// Options for reading Sleeper's <c>/v1</c> responses.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>SnakeCaseLower</c> maps <c>roster_id</c> to <c>RosterId</c> and
        /// <c>players_points</c> to <c>PlayersPoints</c>, which is why no DTO
        /// member needs a <see cref="JsonPropertyNameAttribute"/>.
        /// </para>
        /// <para>
        /// <c>AllowReadingFromString</c> is required, not defensive: Sleeper
        /// returns <c>season</c> as the string <c>"2026"</c> on both
        /// <c>/state/nfl</c> and <c>/league/{id}</c>, and binding that to an
        /// <see cref="int"/> throws without it.
        /// </para>
        /// </remarks>
        internal static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
    }
}
