using FantasyLeague.Seasons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Builds containers shaped the way a host builds them.
    /// </summary>
    internal static class TestServices
    {
        /// <summary>
        /// A container with configuration and logging, and nothing else.
        /// </summary>
        /// <param name="settings">Configuration on top of a league id of <c>L1</c>.</param>
        /// <returns>The service collection.</returns>
        public static IServiceCollection Create(params (string Key, string? Value)[] settings)
        {
            var values = new Dictionary<string, string?> { ["League:CurrentLeagueId"] = "L1" };
            foreach (var (key, value) in settings)
            {
                values[key] = value;
            }

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            services.AddLogging();
            return services;
        }

        /// <summary>
        /// Adds the season service, with null stores unless the test registered its own first.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The same service collection.</returns>
        public static IServiceCollection AddSeasonServiceWithNullStores(this IServiceCollection services)
        {
            services.AddSeasonService();
            services.TryAddSingleton<IFinalizedWeekStore, NullFinalizedWeekStore>();
            services.TryAddSingleton<IPlayerMapStore, NullPlayerMapStore>();
            return services;
        }
    }
}
