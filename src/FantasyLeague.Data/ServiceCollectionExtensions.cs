using FantasyLeague.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        public static IServiceCollection AddFantasyData(this IServiceCollection services, string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            services.AddDbContextFactory<FantasyDataContext>(
                options => options.UseSqlServer(connectionString));

            services.AddHybridCache();

            services.AddSingleton<SqlFinalizedWeekStore>();
            services.AddSingleton<IFinalizedWeekStore, CachedFinalizedWeekStore>();

            return services;
        }
    }
}
