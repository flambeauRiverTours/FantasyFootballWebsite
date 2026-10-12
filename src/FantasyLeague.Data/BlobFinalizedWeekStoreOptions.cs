using System.ComponentModel.DataAnnotations;

namespace FantasyLeague.Data
{
    internal sealed class BlobFinalizedWeekStoreOptions
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
        public string ContainerName { get; set; } = "finalized-week-store";
    }
}
