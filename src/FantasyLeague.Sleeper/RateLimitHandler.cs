using System.Threading.RateLimiting;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// An exception thrown when the local rate limit for Sleeper API requests is exceeded.
    /// </summary>
    /// <param name="message">The exception message.</param>
    internal sealed class SleeperRateLimitExceededException(string message)
        : HttpRequestException(message);

    /// <summary>
    /// A delegating handler that enforces the local rate limit for Sleeper API requests.
    /// </summary>
    /// <param name="limiter">The rate limiter to use.</param>
    internal sealed class RateLimitHandler(RateLimiter limiter) : DelegatingHandler
    {
        /// <summary>
        /// Sends an HTTP request to the inner handler, enforcing the local rate limit.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The response from the rest of the pipeline.</returns>
        /// <exception cref="SleeperRateLimitExceededException">
        /// The bucket was empty and the queue was full.
        /// </exception>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var lease = await limiter.AcquireAsync(1, cancellationToken)
                .ConfigureAwait(false);

            if (!lease.IsAcquired)
            {
                throw new SleeperRateLimitExceededException(
                    "The local Sleeper rate limit refused this request: the token bucket was " +
                    "empty and its queue was full. Something is calling Sleeper far more often " +
                    "than the request budget allows.");
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
