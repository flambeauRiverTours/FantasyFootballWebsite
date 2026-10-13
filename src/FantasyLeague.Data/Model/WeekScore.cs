namespace FantasyLeague.Data.Model
{
    /// <summary>
    /// One roster's finalized week, as stored in SQL.
    /// </summary>
    /// <remarks>
    /// Read whole by key, so most of the week is kept as JSON rather than in
    /// related tables (A14, A20).
    /// </remarks>
    internal sealed class WeekScore
    {
        /// <summary>Gets or sets the league the week belongs to.</summary>
        public string LeagueId { get; set; } = string.Empty;

        /// <summary>Gets or sets the season year.</summary>
        public int Season { get; set; }

        /// <summary>Gets or sets the week number.</summary>
        public int Week { get; set; }

        /// <summary>Gets or sets Sleeper's roster id.</summary>
        public int RosterId { get; set; }

        /// <summary>Gets or sets the points scored, with any override applied.</summary>
        public decimal ActualPts { get; set; }

        /// <summary>Gets or sets the best legal lineup's points.</summary>
        public decimal IdealPts { get; set; }

        /// <summary>Gets or sets the started player ids, in slot order.</summary>
        public string StartersJson { get; set; } = string.Empty;

        /// <summary>Gets or sets every rostered player with their points, so a solver fix can recompute.</summary>
        public string PlayersJson { get; set; } = string.Empty;

        /// <summary>Gets or sets the solver's lineup.</summary>
        public string IdealLineupJson { get; set; } = string.Empty;

        /// <summary>Gets or sets average points per slot, empty when the week was excluded from the radar.</summary>
        public string SlotAveragesJson { get; set; } = string.Empty;

        /// <summary>Gets or sets whether the week's scores are settled.</summary>
        public bool IsFinal { get; set; }

        /// <summary>Gets or sets when the row was written.</summary>
        public DateTimeOffset ComputedUtc { get; set; }
    }
}
