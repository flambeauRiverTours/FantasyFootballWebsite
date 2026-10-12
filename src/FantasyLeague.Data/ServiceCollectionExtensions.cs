using Azure.Storage.Blobs;
using FantasyLeague.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Registers every store: SQL for finalized weeks, and blob storage for the
    /// current season and the player map.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="IFinalizedWeekStore"/> and <see cref="IPlayerMapStore"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="connectionString">The SQL connection string.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddFantasyData(this IServiceCollection services, string? connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            // EnableRetryOnFailure: a serverless database's first connection after
            // auto-pause doesn't make the caller wait, it replies immediately with
            // error 40613 ("not currently available, retry later") while it wakes
            // up in the background - a bigger Connect Timeout does nothing against
            // that, only retrying with backoff does (D7, A16's cold-start follow-up).
            services.AddDbContextFactory<FantasyDataContext>(
                options => options.UseSqlServer(connectionString,
                    sql => sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

            services.AddHybridCache();
            services.TryAddSingleton(TimeProvider.System);

            services.AddOptions<BlobStorageOptions>()
                .BindConfiguration("BlobStorage")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddSingleton<IBlobFinalizedWeekStore>(provider =>
                Container(provider, o => o.FinalizedWeekContainer) is { } container
                    ? new BlobFinalizedWeekStore(container, provider.GetRequiredService<ILogger<BlobFinalizedWeekStore>>())
                    : new NullBlobFinalizedWeekStore());

            services.AddSingleton<IPlayerMapStore>(provider =>
                Container(provider, o => o.PlayerMapContainer) is { } container
                    ? new BlobPlayerMapStore(
                        container,
                        provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value.PlayerMapBlob,
                        provider.GetRequiredService<ILogger<BlobPlayerMapStore>>())
                    : new NullPlayerMapStore());

            services.AddSingleton<SqlFinalizedWeekStore>();
            services.AddSingleton<BlobFirstFinalizedWeekStore>();
            services.AddSingleton<IFinalizedWeekStore, CachedFinalizedWeekStore>();

            return services;
        }

        /// <summary>
        /// Opens a container on the configured account.
        /// </summary>
        /// <param name="provider">The service provider.</param>
        /// <param name="name">Picks the container's name from the options.</param>
        /// <returns>The container, or null when no storage account is configured.</returns>
        /// <remarks>
        /// Falls back to <c>AzureWebJobsStorage</c>, which is already present
        /// wherever a Functions host runs.
        /// </remarks>
        private static BlobContainerClient? Container(IServiceProvider provider, Func<BlobStorageOptions, string> name)
        {
            var options = provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
            var connectionString = options.ConnectionString
                ?? provider.GetRequiredService<IConfiguration>()["AzureWebJobsStorage"];

            return string.IsNullOrWhiteSpace(connectionString)
                ? null
                : new BlobServiceClient(connectionString).GetBlobContainerClient(name(options));
        }
    }
}
