namespace FantasyLeague.Data.Model
{
    /// <summary>
    /// A given user's team
    /// </summary>
    public class Team
    {
        public string LeagueId { get; set; } = string.Empty;
        public int RosterId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string OwnerDisplayName { get; set; } = string.Empty;
        public string OwnerUserId { get; set; } = string.Empty;
    }
}
