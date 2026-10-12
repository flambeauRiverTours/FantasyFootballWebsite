using System.Security.Cryptography;
using System.Text.Json;
using FantasyLeague.Core;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// Turns an assembled season into the §09 response body.
    /// </summary>
    /// <remarks>
    /// A pure function of its arguments, so it can be tested without a Functions
    /// host. The endpoint supplies the clock rather than this reaching for it.
    /// </remarks>
    public static class SeasonPayloadMapper
    {
        /// <summary>
        /// Maps a snapshot to the season response.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <param name="asOfUtc">When the snapshot was assembled.</param>
        /// <returns>The response body, without the snapshot's excluded rosters (A1).</returns>
        public static SeasonPayload ToSeasonPayload(SeasonSnapshot snapshot, DateTimeOffset asOfUtc)
        {
            var teams = snapshot.Teams
                .Where(t => !snapshot.ExcludedRosterIds.Contains(t.RosterId))
                .Select(ToTeam)
                .OrderByDescending(t => t.Totals.Actual)
                .ToList();

            var leagueSlotAverages = SlotAverager.LeagueAverages(teams.Select(t => t.SlotAverages));
            return new SeasonPayload(
                snapshot.LeagueName,
                snapshot.Season,
                true, // only the current season is served until T29 (A19)
                snapshot.CurrentWeek,
                snapshot.FinalThroughWeek,
                asOfUtc,
                SlotOrder(snapshot.Slots),
                leagueSlotAverages,
                teams);
        }

        /// <summary>
        /// Maps the seasons that can be served.
        /// </summary>
        /// <param name="current">The live season.</param>
        /// <param name="available">Every season with data, or null for just the current one.</param>
        /// <returns>The response body.</returns>
        public static SeasonsPayload ToSeasonsPayload(int current, IEnumerable<int>? available = null) =>
            new(current, [.. (available ?? [current]).Distinct().OrderByDescending(s => s)]);

        /// <summary>
        /// Reduces the startable slots to the distinct codes, in roster_positions order.
        /// </summary>
        /// <param name="slots">Every startable slot, duplicates included.</param>
        /// <returns>The radar's axis order.</returns>
        /// <remarks>
        /// Eleven slots become eight axes for this league. R2: the silhouette
        /// depends on axis order, so this order is fixed and never re-sorted.
        /// </remarks>
        private static IReadOnlyList<string> SlotOrder(IReadOnlyList<Slot> slots) =>
            [.. slots.Select(s => s.Code).Distinct().Select(Slot.DisplayCode)];

        /// <summary>
        /// Maps one roster's season.
        /// </summary>
        /// <param name="team">The roster.</param>
        /// <returns>The team entry.</returns>
        private static TeamPayload ToTeam(TeamSeason team)
        {
            var spread = SpreadCalculator.Calculate(
                [.. team.Weeks.Where(w => w.IsFinal).Select(w => w.ActualPts)]);

            var totals = new TotalsPayload(
                team.Weeks.Sum(w => w.ActualPts),
                team.Weeks.Sum(w => w.IdealPts),
                spread?.Sigma,
                spread?.Cv,
                spread?.Mean,
                spread?.Min,
                spread?.Max);

            return new TeamPayload(
                team.RosterId,
                team.Name,
                totals,
                SlotAverager.SeasonAverages(team.Weeks)
                    .ToDictionary(kv => Slot.DisplayCode(kv.Key), kv => kv.Value),
                [.. team.Weeks
                    .OrderBy(w => w.Week)
                    .Select(w => new WeekPayload(w.Week, w.ActualPts, w.IdealPts, w.IsFinal))]);
        }

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
        public static string ComputeETag(SeasonPayload payload)
        {
            var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload with { AsOfUtc = default }));

            // 16 hex characters is plenty for a weak tag
            return $"W/\"{payload.Season}-wk{payload.CurrentWeek}-{Convert.ToHexString(hash)[..16]}\"";
        }

        /// <summary>
        /// Decides whether a conditional request already holds this version.
        /// </summary>
        /// <param name="ifNoneMatch">The request's If-None-Match header.</param>
        /// <param name="etag">The tag from <see cref="ComputeETag"/>.</param>
        /// <returns><c>true</c> when a 304 is the right answer.</returns>
        /// <remarks>
        /// The header is a comma separated list and a client may send the tag
        /// without its W/ prefix, so neither a string comparison nor a Contains
        /// call is enough.
        /// </remarks>
        public static bool MatchesETag(StringValues ifNoneMatch, string etag)
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
