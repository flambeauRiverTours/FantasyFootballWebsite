using System.ComponentModel.DataAnnotations;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Where the slim player map is kept and how long it stays good for, bound
    /// from the <c>PlayerMap</c> configuration section.
    /// </summary>
    internal sealed class PlayerMapOptions
    {
        /// <summary>
        /// Gets or sets the storage connection string. Falls back to
        /// <c>AzureWebJobsStorage</c>, and with neither set the map is not
        /// persisted at all.
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the container the map blob lives in.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string ContainerName { get; set; } = "player-map";

        /// <summary>
        /// Gets or sets the blob name.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string BlobName { get; set; } = "players.json";

        /// <summary>
        /// Gets or sets how old a map may be before it is refetched from Sleeper.
        /// </summary>
        [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
        public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);
    }
}
