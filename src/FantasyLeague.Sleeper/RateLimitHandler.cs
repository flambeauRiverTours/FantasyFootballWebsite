using System.Threading.RateLimiting;

namespace FantasyLeague.Sleeper
{
    /// <summary>
    /// Thrown when this application's own rate limit refuses a request, before it
    /// reaches the network.
    /// </summary>
    /// <remarks>
    /// Derives from <see cref="HttpRequestException"/> so callers already handling
    /// HTTP failures are not surprised by a third exception type, but it is a
    /// distinct type so the retry policy can tell it apart. Retrying it is
    /// pointless: nothing on the far side has failed, and the bucket refills on a
    /// timer that a few seconds of backoff will not outlast.
    /// </remarks>
    internal sealed class SleeperRateLimitExceededException(string message)
        : HttpRequestException(message);

    /// <summary>
    /// A <see cref="DelegatingHandler"/> that spends a token from a shared
    /// <see cref="RateLimiter"/> before letting a request reach Sleeper.
    /// </summary>
    /// <remarks>
    /// This is the structural guarantee behind the request budget: nothing can
    /// exceed the limit because nothing reaches Sleeper except through here.
    /// <see cref="SleeperClient"/> holds only an <see cref="HttpClient"/>, so it
    /// has no way to reach a socket that bypasses this chain.
    /// <para>
    /// Handler instances are pooled and shared across concurrent requests for the
    /// lifetime of a handler chain, so this type holds no per-request state.
    /// </para>
    /// </remarks>
    /// <param name="limiter">The shared bucket. Must be a singleton — a per-request limiter is not a limiter.</param>
    internal sealed class RateLimitHandler(RateLimiter limiter) : DelegatingHandler
    {
        /// <summary>
        /// Acquires a token, then passes the request to the next handler.
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
            // AcquireAsync does not throw when it refuses. It waits if the queue
            // has room, and otherwise hands back a lease that says no, which is
            // only visible through IsAcquired.
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
