using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Extension methods for <see cref="IServiceCollection"/> to add the <see cref="SleeperClient"/> service.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>Burst capacity: the most requests that can go out back to back.</summary>
        private const int TokenLimit = 50;

        /// <summary>How many tokens are dropped into the bucket each period, as a lump rather than a trickle.</summary>
        private const int TokensPerPeriod = 50;

        /// <summary>How many callers may wait for the next refill before requests are refused outright.</summary>
        private const int QueueLimit = 20;

        /// <summary>
        /// Registers <see cref="SleeperClient"/> together with the whole outbound
        /// pipeline: resilience on the outside, this application's rate limit on
        /// the inside.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configureResilience">
        /// Optional overrides applied after the defaults below, for a host that
        /// needs different settings or a test that cannot wait out real backoff.
        /// </param>
        /// <returns>The HTTP client builder, so a caller may add further handlers.</returns>
        /// <remarks>
        /// The pipeline is assembled here rather than by the host so that the
        /// client cannot be registered without its rate limit. Everything it needs
        /// is internal to this assembly as a result.
        /// </remarks>
        public static IHttpClientBuilder AddSleeperClient(
            this IServiceCollection services,
            Action<HttpStandardResilienceOptions>? configureResilience = null)
        {

            services.TryAddSingleton<RateLimiter>(_ => new TokenBucketRateLimiter(
                new TokenBucketRateLimiterOptions
                {
                    TokenLimit = TokenLimit,
                    TokensPerPeriod = TokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }));


            services.TryAddTransient<RateLimitHandler>();

            var builder = services.AddHttpClient<SleeperClient>(c =>
            {
                c.BaseAddress = new Uri("https://api.sleeper.app/v1/");
                c.DefaultRequestHeaders.UserAgent.ParseAdd("bosty-base-site/1.0");
                //No timeout - this is handled by the resilience policy below, which retries on timeouts and other transient failures.
            });

            builder.AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.UseJitter = true;
                o.Retry.ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome));

                configureResilience?.Invoke(o);
            });

            builder.AddHttpMessageHandler<RateLimitHandler>();

            //Add the hybrid cache to the service collection, so that it can be used by the SleeperClient and other services.
            services.AddHybridCache();
            services.TryAddTransient<SleeperReaderCache>();

            // singleton: the map it holds is shared state
            services.TryAddSingleton<PlayerMapRefresher>();

            return builder;
        }

        /// <summary>
        /// Decides whether a failed attempt is worth repeating.
        /// </summary>
        /// <param name="outcome">The result or exception from the attempt.</param>
        /// <returns><c>true</c> when retrying could plausibly succeed.</returns>
        private static bool ShouldRetry(Polly.Outcome<HttpResponseMessage> outcome)
        {
            if (outcome.Exception is SleeperRateLimitExceededException) return false;

            if (outcome.Result is { } response)
            {
                return response.StatusCode is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.RequestTimeout
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.GatewayTimeout;
            }

            return outcome.Exception is HttpRequestException or TimeoutException;
        }
    }
}
