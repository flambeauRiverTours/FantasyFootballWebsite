using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Extension methods for <see cref="IServiceCollection"/> to add the <see cref="SleeperClient"/> service.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the <see cref="SleeperClient"/> service to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The HTTP client builder.</returns>
        public static IHttpClientBuilder AddSleeperClient(this IServiceCollection services) =>
          services.AddHttpClient<SleeperClient>(c =>
          {
              c.BaseAddress = new Uri("https://api.sleeper.app/v1/");
              c.DefaultRequestHeaders.UserAgent.ParseAdd("bosty-base-site/1.0");
          });
    }
}
