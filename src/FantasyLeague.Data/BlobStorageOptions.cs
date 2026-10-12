using System.ComponentModel.DataAnnotations;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Where the blob stores keep their data, bound from the <c>BlobStorage</c>
    /// configuration section.
    /// </summary>
    internal sealed class BlobStorageOptions
    {
        /// <summary>
        /// Gets or sets the storage connection string. Falls back to
        /// <c>AzureWebJobsStorage</c>, and with neither set nothing is persisted.
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the container the player map blob lives in.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string PlayerMapContainer { get; set; } = "player-map";

        /// <summary>
        /// Gets or sets the player map blob's name.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string PlayerMapBlob { get; set; } = "players.json";

        /// <summary>
        /// Gets or sets the container the current season's blob lives in.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string FinalizedWeekContainer { get; set; } = "finalized-week-store";
    }
}
