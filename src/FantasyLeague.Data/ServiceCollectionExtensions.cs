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
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds fantasy data to the services
        /// </summary>
        /// <param name="services">Service collection to add data to</param>
        /// <param name="connectionString">Connection string to the sql server</param>
        /// <returns>Updated services collection</returns>
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

            // bound through the provider rather than BindConfiguration
            services.AddOptions<BlobFinalizedWeekStoreOptions>()
                .Configure<IServiceProvider>((options, provider) =>
                    provider.GetService<IConfiguration>()?.GetSection("FinalizedWeekBlob").Bind(options))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // AzureWebJobsStorage is already present wherever a Functions host runs
            services.TryAddSingleton<IBlobFinalizedWeekStore>(provider =>
            {
                var blobStorage = provider.GetRequiredService<IOptions<BlobFinalizedWeekStoreOptions>>().Value;
                var connectionString = blobStorage.ConnectionString
                    ?? provider.GetService<IConfiguration>()?["AzureWebJobsStorage"];

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return new NullBlobFinalizedWeekStore();
                }

                return new BlobFinalizedWeekStore(
                    new BlobServiceClient(connectionString).GetBlobContainerClient(blobStorage.ContainerName),
                    provider.GetRequiredService<ILogger<BlobFinalizedWeekStore>>());
            });

            services.TryAddSingleton(TimeProvider.System);

            services.AddSingleton<SqlFinalizedWeekStore>();
            services.AddSingleton<BlobFirstFinalizedWeekStore>();
            services.AddSingleton<IFinalizedWeekStore, CachedFinalizedWeekStore>();

            return services;
        }
    }
}
