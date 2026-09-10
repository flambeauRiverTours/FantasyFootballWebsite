using System.Net;
using System.Threading.RateLimiting;
using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Counts how many requests actually reach the socket, and answers with
    /// whatever status the test asks for.
    /// </summary>
    internal sealed class CountingHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// The handler in isolation: does it spend a token, and does it refuse when
    /// there are none left?
    /// </summary>
    public class RateLimitHandlerTests
    {
        private static HttpClient ClientWith(RateLimiter limiter, CountingHandler socket) =>
            new(new RateLimitHandler(limiter) { InnerHandler = socket })
            {
                BaseAddress = new Uri("https://api.sleeper.app/v1/"),
            };

        private static TokenBucketRateLimiter Bucket(int tokens, int queue) =>
            new(new TokenBucketRateLimiterOptions
            {
                TokenLimit = tokens,
                TokensPerPeriod = tokens,
                // Long enough that nothing refills mid-test.
                ReplenishmentPeriod = TimeSpan.FromHours(1),
                QueueLimit = queue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = false,
            });

        [Fact]
        public async Task A_request_passes_through_while_tokens_remain()
        {
            using var limiter = Bucket(tokens: 2, queue: 0);
            var socket = new CountingHandler();
            using var http = ClientWith(limiter, socket);

            await http.GetAsync("state/nfl");
            await http.GetAsync("state/nfl");

            socket.Attempts.Should().Be(2);
        }

        [Fact]
        public async Task An_exhausted_bucket_refuses_before_reaching_the_network()
        {
            // The point of the fuse: the request is stopped here, not sent and
            // then apologised for.
            using var limiter = Bucket(tokens: 1, queue: 0);
            var socket = new CountingHandler();
            using var http = ClientWith(limiter, socket);

            await http.GetAsync("state/nfl");

            await FluentActions.Awaiting(() => http.GetAsync("state/nfl"))
                .Should().ThrowAsync<HttpRequestException>();

            socket.Attempts.Should().Be(1, "the refused request must never reach Sleeper");
        }

        [Fact]
        public async Task The_refusal_is_its_own_exception_type()
        {
            // Distinct so the retry policy can exclude it, but still an
            // HttpRequestException so existing catch blocks are not surprised.
            using var limiter = Bucket(tokens: 1, queue: 0);
            using (await limiter.AcquireAsync(1)) { }   // TokenLimit must be > 0, so drain it
            var socket = new CountingHandler();
            using var http = ClientWith(limiter, socket);

            var thrown = await FluentActions.Awaiting(() => http.GetAsync("state/nfl"))
                .Should().ThrowAsync<HttpRequestException>();

            thrown.Which.GetType().Name.Should().Be("SleeperRateLimitExceededException");
            thrown.Which.Message.Should().Contain("request budget");
        }

        [Fact]
        public async Task A_cancelled_request_does_not_spend_a_token()
        {
            using var limiter = Bucket(tokens: 1, queue: 0);
            var socket = new CountingHandler();
            using var http = ClientWith(limiter, socket);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await FluentActions.Awaiting(() => http.GetAsync("state/nfl", cts.Token))
                .Should().ThrowAsync<OperationCanceledException>();

            // The token survived, so a later call still gets through.
            await http.GetAsync("state/nfl");
            socket.Attempts.Should().Be(1);
        }
    }

    /// <summary>
    /// The assembled pipeline: resilience outside, rate limit inside.
    /// </summary>
    public class SleeperPipelineTests
    {
        private static ServiceProvider ProviderWith(CountingHandler socket, RateLimiter? limiter = null)
        {
            var services = new ServiceCollection();
            if (limiter is not null) services.AddSingleton(limiter);

            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                .ConfigurePrimaryHttpMessageHandler(() => socket);

            return services.BuildServiceProvider();
        }

        private static HttpClient Client(ServiceProvider provider) =>
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SleeperClient));

        [Fact]
        public async Task The_rate_limiter_is_actually_in_the_pipeline()
        {
            // Registering a handler in DI does not put it in the chain. This is
            // the test that would have caught that: with no tokens at all, a
            // request must fail rather than sail through.
            using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 1,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromHours(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = false,
            });
            using (await limiter.AcquireAsync(1)) { }   // drain it; the bucket may not start empty
            var socket = new CountingHandler();
            using var provider = ProviderWith(socket, limiter);

            await FluentActions.Awaiting(() => Client(provider).GetAsync("state/nfl"))
                .Should().ThrowAsync<HttpRequestException>();

            socket.Attempts.Should().Be(0, "nothing may reach Sleeper without a token");
        }

        [Fact]
        public async Task Every_retry_spends_its_own_token()
        {
            // Handler order decides this. Resilience is added first and is
            // therefore outermost, so each retry passes back down through the
            // limiter. Reverse the order and one token would cover four network
            // requests -- exactly the retry-loop case the limit exists to contain.
            //
            // Three tokens, no queue, and a server that always fails: the first
            // three attempts spend the three tokens, and the fourth is refused by
            // the limiter rather than sent.
            using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 3,
                TokensPerPeriod = 3,
                ReplenishmentPeriod = TimeSpan.FromHours(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = false,
            });
            var socket = new CountingHandler(HttpStatusCode.ServiceUnavailable);
            using var provider = ProviderWith(socket, limiter);

            try
            {
                await Client(provider).GetAsync("state/nfl");
            }
            catch
            {
                // The pipeline gives up somehow; what it did on the way is the point.
            }

            socket.Attempts.Should().BeLessThanOrEqualTo(3,
                "a fourth attempt would mean retries were bypassing the token bucket");
        }

        [Fact]
        public async Task A_retryable_status_is_retried()
        {
            var socket = new CountingHandler(HttpStatusCode.ServiceUnavailable);
            using var provider = ProviderWith(socket);

            try { await Client(provider).GetAsync("state/nfl"); } catch { }

            socket.Attempts.Should().BeGreaterThan(1, "503 is transient and worth repeating");
        }

        [Fact]
        public async Task A_client_error_is_not_retried()
        {
            // 404 will not become a 200 however many times it is asked.
            var socket = new CountingHandler(HttpStatusCode.NotFound);
            using var provider = ProviderWith(socket);

            try { await Client(provider).GetAsync("state/nfl"); } catch { }

            socket.Attempts.Should().Be(1);
        }

        [Fact]
        public void The_default_bucket_is_a_singleton()
        {
            // The bucket is the shared state. Two instances would each believe
            // they held the whole budget.
            var services = new ServiceCollection();
            services.AddSleeperClient();
            using var provider = services.BuildServiceProvider();

            var first = provider.GetRequiredService<RateLimiter>();
            var second = provider.GetRequiredService<RateLimiter>();

            first.Should().BeSameAs(second);
        }

        [Fact]
        public void Registering_twice_does_not_create_a_second_bucket()
        {
            var services = new ServiceCollection();
            services.AddSleeperClient();
            services.AddSleeperClient();
            using var provider = services.BuildServiceProvider();

            provider.GetServices<RateLimiter>().Should().ContainSingle();
        }
    }
}
