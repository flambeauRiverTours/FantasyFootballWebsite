using System;
using System.Collections.Generic;
using System.Text;

namespace FantasyLeague.Data
{
    public sealed class BlobFinalizedWeekStoreOptions
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
        public string ContainerName { get; set; } = "finalized-week-store";
    }
}
