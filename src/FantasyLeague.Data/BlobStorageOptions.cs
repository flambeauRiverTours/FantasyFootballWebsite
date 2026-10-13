using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Where the blob stores keep their data, bound from the <c>BlobStorage</c>
    /// configuration section.
    /// </summary>
    internal sealed class BlobStorageOptions
    {
        /// <summary>
        /// Gets or sets a storage connection string, for Azurite in local
        /// development. Falls back to <c>AzureWebJobsStorage</c>.
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the blob endpoint reached by managed identity, as in Azure,
        /// where the account accepts no keys. Falls back to the account the
        /// Functions host names in <c>AzureWebJobsStorage__accountName</c>.
        /// </summary>
        public Uri? ServiceUri { get; set; }

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

        /// <summary>
        /// Fills whichever of the two the section left unset from the Functions
        /// host's own storage setting.
        /// </summary>
        /// <param name="configuration">The app's configuration.</param>
        /// <remarks>
        /// <c>AzureWebJobsStorage</c> is a connection string locally and, in
        /// Azure, a section whose <c>accountName</c> means "connect by identity".
        /// </remarks>
        internal void FallBackTo(IConfiguration configuration)
        {
            if (ConnectionString is not null || ServiceUri is not null)
            {
                return;
            }

            var host = configuration.GetSection("AzureWebJobsStorage");
            if (host.Value is { Length: > 0 } connectionString)
            {
                ConnectionString = connectionString;
            }
            else if (host["accountName"] is { Length: > 0 } account)
            {
                ServiceUri = new Uri($"https://{account}.blob.core.windows.net");
            }
        }
    }
}
