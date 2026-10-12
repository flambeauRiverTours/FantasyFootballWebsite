using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Registers the Sleeper client and the reader in front of it.
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
        /// Registers <see cref="ISleeperReader"/> and the client behind it, together
        /// with the whole outbound pipeline: resilience on the outside, this
        /// application's rate limit on the inside.
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
                c.DefaultRequestHeaders.UserAgent.ParseAdd("ideal-points/1.0");
                // the resilience pipeline's 30 s total timeout fires well inside HttpClient's own 100 s
            });

            // the default logger writes request URLs, and every league URL carries the league id (A2)
            builder.RemoveAllLoggers();

            builder.AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.UseJitter = true;
                o.Retry.ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome));

                configureResilience?.Invoke(o);
            });

            builder.AddHttpMessageHandler<RateLimitHandler>();

            services.AddHybridCache();
            services.TryAddTransient<ISleeperReader, SleeperReader>();

            return builder;
        }

        /// <summary>
        /// Decides whether a failed attempt is worth repeating.
        /// </summary>
        /// <param name="outcome">The result or exception from the attempt.</param>
        /// <returns><c>true</c> for the standard transient failures, except the local limiter's refusal.</returns>
        /// <remarks>
        /// The refusal is an <see cref="HttpRequestException"/>, which the standard
        /// predicate retries. Retrying it only spends the queue.
        /// </remarks>
        private static bool ShouldRetry(Polly.Outcome<HttpResponseMessage> outcome) =>
            outcome.Exception is not SleeperRateLimitExceededException
            && HttpClientResiliencePredicates.IsTransient(outcome);
    }
}
