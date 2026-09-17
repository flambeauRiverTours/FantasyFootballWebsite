namespace FantasyLeague.Data.Model
{
    /// <summary>
    /// Represents one set of computations for one team for one week of one season in one league
    /// </summary>
    public class WeekScore
    {
        public string LeagueId { get; set; } = string.Empty;
        public int Season { get; set; } = 0;
        public int Week { get; set; } = 0;
        public decimal ActualPts { get; set; } = 0;
        public int RosterId { get; set; }
        public decimal IdealPts { get; set; } = 0;
        public string StartersJson { get; set; } = string.Empty;
        public string PlayersPointsJson { get; set; } = string.Empty;
        public string IdealLineupJson { get; set; } = string.Empty;
        public string SlotAveragesJson { get; set; } = string.Empty;
        public bool IsFinal { get; set; } //set after a week is finalized
        public DateTimeOffset ComputedUtc { get; set; }
    }
}
