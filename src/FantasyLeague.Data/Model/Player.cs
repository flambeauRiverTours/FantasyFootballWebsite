namespace FantasyLeague.Data.Model
{
    /// <summary>
    /// Represents a given player
    /// </summary>
    public class Player
    {
        public string PlayerId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        /// <summary>
        /// The team a player belongs to. Can be null
        /// </summary>
        public string? Team { get; set; }
        public string PositionsJson { get; set; } = string.Empty;
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
