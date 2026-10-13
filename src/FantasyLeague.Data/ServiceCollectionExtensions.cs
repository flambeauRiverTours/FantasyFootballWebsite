using Azure.Identity;
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

            // AzureWebJobsStorage is already present wherever a Functions host runs.
            // Without any storage, start-up fails: running on without blobs would
            // fetch the 14 MB player dump on every cold instance (A12)
            services.AddOptions<BlobStorageOptions>()
                .BindConfiguration("BlobStorage")
                .PostConfigure<IConfiguration>((o, configuration) => o.FallBackTo(configuration))
                .ValidateDataAnnotations()
                .Validate(
                    o => o.ConnectionString is not null || o.ServiceUri is not null,
                    "No storage account: set BlobStorage:ConnectionString or BlobStorage:ServiceUri, or AzureWebJobsStorage.")
                .ValidateOnStart();

            services.AddSingleton(provider =>
                new BlobFinalizedWeekStore(
                    Container(provider, o => o.FinalizedWeekContainer),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<ILogger<BlobFinalizedWeekStore>>()));

            services.AddSingleton<IPlayerMapStore>(provider =>
                new BlobPlayerMapStore(
                    Container(provider, o => o.PlayerMapContainer),
                    provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value.PlayerMapBlob,
                    provider.GetRequiredService<ILogger<BlobPlayerMapStore>>()));

            services.AddSingleton<SqlFinalizedWeekStore>();
            services.AddSingleton<IFinalizedWeekStore, FinalizedWeekStore>();

            return services;
        }

        /// <summary>
        /// Opens a container on the configured account.
        /// </summary>
        /// <param name="provider">The service provider.</param>
        /// <param name="name">Picks the container's name from the options.</param>
        /// <returns>The container.</returns>
        /// <remarks>
        /// By connection string locally; by the Function App's own identity in
        /// Azure, where the account has shared keys turned off.
        /// </remarks>
        private static BlobContainerClient Container(IServiceProvider provider, Func<BlobStorageOptions, string> name)
        {
            var options = provider.GetRequiredService<IOptions<BlobStorageOptions>>().Value;

            var service = options.ConnectionString is { } connectionString
                ? new BlobServiceClient(connectionString)
                : new BlobServiceClient(options.ServiceUri, new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));

            return service.GetBlobContainerClient(name(options));
        }
    }
}
