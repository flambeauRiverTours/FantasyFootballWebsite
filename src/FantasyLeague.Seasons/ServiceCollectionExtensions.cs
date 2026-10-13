using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// Registers the season assembly and the player map it reads.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="ISeasonService"/> and <see cref="IPlayerMapRefresher"/>,
        /// bound to the configured league.
        /// </summary>
        /// <returns>The same service collection.</returns>
        /// <remarks>
        /// Needs <c>AddSleeperClient</c>, plus an <c>IFinalizedWeekStore</c> and an
        /// <c>IPlayerMapStore</c> from the host.
        /// </remarks>
        public static IServiceCollection AddSeasonService(this IServiceCollection services)
        {
            services.AddOptions<LeagueOptions>()
                .BindConfiguration("League")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<PlayerMapOptions>()
                .BindConfiguration("PlayerMap")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddHybridCache();
            services.TryAddSingleton(TimeProvider.System);

            // singleton: the map it holds is shared state
            services.TryAddSingleton<IPlayerMapRefresher, PlayerMapRefresher>();
            services.TryAddSingleton<ReportedProblems>();
            services.TryAddTransient<ISeasonService, SeasonService>();

            return services;
        }
    }
}
