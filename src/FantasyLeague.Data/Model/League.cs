namespace FantasyLeague.Data.Model
{
    /// <summary>
    /// Represents one season of one league
    /// </summary>
    public class League
    {
        public string LeagueId { get; set; } = string.Empty;
        /// <summary>
        /// pointer back to the previous season's version of this league
        /// </summary>
        public string? PreviousLeagueId { get; set; }
        public int Season { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset UpdatedUtc { get; set; }
        public string RosterPositionsJson { get; set; } = string.Empty;
    }
}
