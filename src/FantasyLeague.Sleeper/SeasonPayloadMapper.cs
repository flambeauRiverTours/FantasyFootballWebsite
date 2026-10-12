using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FantasyLeague.Core;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Turns an assembled season into the §09 response body.
    /// </summary>
    /// <remarks>
    /// A pure function of its arguments, so it can be tested without a Functions
    /// host. The endpoint supplies the clock and the configured owner ids rather
    /// than this reaching for either.
    /// </remarks>
    public static class SeasonPayloadMapper
    {
        /// <summary>
        /// Maps a snapshot to the season response.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <param name="asOfUtc">When the snapshot was assembled.</param>
        /// <param name="requestedSeason">The season asked for, or null for the current one.</param>
        /// <param name="excludedRosterIds">Rosters to leave out entirely (A1).</param>
        /// <param name="overrideLeagueName">Name of the league</param>
        /// <returns>The response body.</returns>
        public static SeasonPayload ToIndividualSeasonPayload(
            SeasonSnapshot snapshot,
            DateTimeOffset asOfUtc,
            int? requestedSeason = null,
            IReadOnlySet<int>? excludedRosterIds = null,
            string overrideLeagueName = "")
        {
            var excluded = excludedRosterIds ?? new HashSet<int>();

            var teams = snapshot.Teams
                .Where(t => !excluded.Contains(t.RosterId))
                .Select(ToTeam)
                .OrderByDescending(t => t.Totals.Actual)
                .ToList();

            var leagueSlotAverages = SlotAverager.LeagueAverages(teams.Select(t => t.SlotAverages));
            var name = string.IsNullOrWhiteSpace(overrideLeagueName) ? snapshot.LeagueName : overrideLeagueName;
            return new SeasonPayload(
                name,
                snapshot.Season,
                requestedSeason is null || requestedSeason == snapshot.Season,
                snapshot.CurrentWeek,
                snapshot.FinalThroughWeek,
                asOfUtc,
                SlotOrder(snapshot.Slots),
                leagueSlotAverages,
                teams);
        }

        /// <summary>
        /// Resolves configured owner ids to this season's roster ids.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <param name="ownerIds">The configured owner ids. Empty excludes nobody.</param>
        /// <returns>The rosters to exclude, and every owner id no roster matched.</returns>
        public static ExcludedRosters ResolveExcludedRosters(SeasonSnapshot snapshot, IEnumerable<string> ownerIds)
        {
            var owners = ownerIds as IReadOnlyCollection<string> ?? [.. ownerIds];
            if (owners.Count == 0)
            {
                return new ExcludedRosters(new HashSet<int>(), []);
            }

            var matched = snapshot.RosterOwnerIds.Where(pair => owners.Contains(pair.Value, StringComparer.Ordinal)).ToList();

            return new ExcludedRosters(
                matched.Select(pair => pair.Key).ToHashSet(),
                [.. owners.Except(matched.Select(pair => pair.Value), StringComparer.Ordinal)]);
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
                [.. team.Weeks.Where(w => w.IsFinal).Select(w => w.Actual)]);

            var totals = new TotalsPayload(
                team.Weeks.Sum(w => w.Actual),
                team.Weeks.Sum(w => w.Ideal),
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
                    .Select(w => new WeekPayload(w.Week, w.Actual, w.Ideal, w.IsFinal))]);
        }

        /// <summary>
        /// Builds a validator for one snapshot.
        /// </summary>
        /// <param name="snapshot">The assembled season.</param>
        /// <returns>A weak entity tag.</returns>
        /// <remarks>
        /// Weak because the payload carries asOfUtc, which differs on every
        /// response; a strong tag would promise byte-identical content it cannot
        /// deliver. The hash covers every week's score, so it changes the moment
        /// Sunday's numbers do.
        /// </remarks>
        public static string ComputeETag(SeasonSnapshot snapshot)
        {
            var builder = new StringBuilder()
                .Append(snapshot.Season).Append('-')
                .Append(snapshot.CurrentWeek).Append('-')
                .Append(snapshot.FinalThroughWeek);

            foreach (var team in snapshot.Teams.OrderBy(t => t.RosterId))
            {
                builder.Append('|').Append(team.RosterId);

                foreach (var week in team.Weeks.OrderBy(w => w.Week))
                {
                    builder.Append(':').Append(week.Week)
                        .Append(',').Append(week.Actual.ToString(CultureInfo.InvariantCulture))
                        .Append(',').Append(week.Ideal.ToString(CultureInfo.InvariantCulture))
                        .Append(',').Append(week.IsFinal ? '1' : '0');

                    // M6: slot attribution can change independently of Actual/Ideal
                    // (the mid-season rules guard), so it needs its own hash input
                    foreach (var slot in week.SlotAverages.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    {
                        builder.Append(';').Append(slot.Key)
                            .Append('=').Append(slot.Value.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            // The hash is 32 bytes, but we only need 16 for a weak tag. The rest is ignored
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

            return $"W/\"{snapshot.Season}-wk{snapshot.CurrentWeek}-{Convert.ToHexString(hash)[..16]}\"";
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
