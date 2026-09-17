using System.ComponentModel.DataAnnotations;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// The one league this site serves, bound from the <c>League</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Never a request parameter: a league id in a query string would make this an
    /// open relay against Sleeper's rate limit. The value is a secret and lives in
    /// local.settings.json, user-secrets or an App Setting, never in the repo.
    /// </remarks>
    public sealed class LeagueOptions
    {
        /// <summary>
        /// Gets or sets the current season's Sleeper league id.
        /// </summary>
        /// <remarks>
        /// Bound from <c>League:CurrentLeagueId</c>, written
        /// <c>League__CurrentLeagueId</c> as an environment variable or App Setting.
        /// Prior seasons are not configured; D8 walks <c>previous_league_id</c> back
        /// from this one.
        /// </remarks>
        [Required(AllowEmptyStrings = false)]
        public string CurrentLeagueId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the owning user ids of rosters left out of every metric.
        /// </summary>
        /// <remarks>
        /// A1. Matched on user id because team names change on a whim and roster
        /// ids are reassigned between seasons. Empty for a league with no
        /// placeholder team.
        /// </remarks>
        public IList<string> ExcludedOwnerIds { get; } = [];

        /// <summary>
        /// Gets or set the string to use as the league name, regardless of what's on sleeper's server
        /// </summary>
        public string OverrideLeagueName { get; set; } = string.Empty;
    }
}
