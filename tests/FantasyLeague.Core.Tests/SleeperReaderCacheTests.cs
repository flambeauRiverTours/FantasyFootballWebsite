using System.Net;
using System.Text;
using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Counts requests that reach the socket, optionally slowly, so concurrent
    /// callers genuinely overlap.
    /// </summary>
    internal sealed class CountingSocket(string? body = null, TimeSpan delay = default) : HttpMessageHandler
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    body ?? SmallestBodyFor(request.RequestUri!.AbsolutePath),
                    Encoding.UTF8, "application/json"),
            };
        }

        /// <summary>The least JSON that binds for the type each path returns.</summary>
        private static string SmallestBodyFor(string path) =>
            path.EndsWith("state/nfl", StringComparison.Ordinal) ? "{\"season\":\"2026\",\"week\":1}" :
            path.EndsWith("players/nfl", StringComparison.Ordinal) ? "{}" :
            path.Contains("/users", StringComparison.Ordinal) ||
            path.Contains("/rosters", StringComparison.Ordinal) ||
            path.Contains("/matchups/", StringComparison.Ordinal) ? "[]" :
            "{\"league_id\":\"L1\",\"season\":\"2026\"}";
    }

    /// <summary>
    /// The caching layer, exercised through the real pipeline so a cache miss
    /// genuinely reaches a socket.
    /// </summary>
    public class SleeperReaderCacheTests
    {
        private static (SleeperReaderCache Reader, CountingSocket Socket, ServiceProvider Provider) Build(
            string? body = null, TimeSpan delay = default)
        {
            var socket = new CountingSocket(body, delay);
            var services = new ServiceCollection();
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                    .ConfigurePrimaryHttpMessageHandler(() => socket);

            var provider = services.BuildServiceProvider();
            return (provider.GetRequiredService<SleeperReaderCache>(), socket, provider);
        }

        [Fact]
        public async Task A_second_call_inside_the_window_does_not_reach_sleeper()
        {
            var (reader, socket, provider) = Build();
            using (provider)
            {
                await reader.GetMatchupsAsync("L1", 1, default);
                await reader.GetMatchupsAsync("L1", 1, default);

                socket.Attempts.Should().Be(1);
            }
        }

        [Fact]
        public async Task Twenty_simultaneous_callers_produce_one_fetch()
        {
            // The whole reason for HybridCache over IMemoryCache. Without
            // stampede protection, twenty concurrent misses on the same key are
            // twenty requests -- which is how a 2-per-minute design becomes a
            // 200-per-minute incident for the length of one round trip, at
            // exactly the moment a Sunday afternoon is busiest.
            var (reader, socket, provider) = Build(delay: TimeSpan.FromMilliseconds(50));
            using (provider)
            {
                await Task.WhenAll(Enumerable.Range(0, 20)
                    .Select(_ => reader.GetMatchupsAsync("L1", 1, default).AsTask()));

                socket.Attempts.Should().Be(1, "nineteen callers should have awaited the first fetch");
            }
        }

        [Fact]
        public async Task Different_weeks_are_cached_separately()
        {
            var (reader, socket, provider) = Build();
            using (provider)
            {
                await reader.GetMatchupsAsync("L1", 1, default);
                await reader.GetMatchupsAsync("L1", 2, default);
                await reader.GetMatchupsAsync("L1", 1, default);

                socket.Attempts.Should().Be(2, "week 1 was served from cache the second time");
            }
        }

        [Fact]
        public async Task Different_leagues_are_cached_separately()
        {
            // Only one league is ever configured today, but a key that ignored
            // the league would serve one league's data for another the day D8's
            // chain walk introduces a second.
            var (reader, socket, provider) = Build();
            using (provider)
            {
                await reader.GetMatchupsAsync("L1", 1, default);
                await reader.GetMatchupsAsync("L2", 1, default);

                socket.Attempts.Should().Be(2);
            }
        }

        [Fact]
        public async Task Each_endpoint_has_its_own_key()
        {
            // Six endpoints, six distinct keys. A collision would serve one
            // payload where another was asked for, and the deserialiser would
            // most likely succeed at producing nonsense rather than throwing.
            var (reader, socket, provider) = Build();
            using (provider)
            {
                await reader.GetMatchupsAsync("L1", 1, default);
                await reader.GetRostersAsync("L1", default);
                await reader.GetUsersAsync("L1", default);
                await reader.GetLeagueAsync("L1", default);
                await reader.GetStateAsync(default);
                await reader.GetPlayersAsync(default);

                socket.Attempts.Should().Be(6);
            }
        }

        [Fact]
        public async Task Repeating_every_endpoint_reaches_sleeper_no_further()
        {
            var (reader, socket, provider) = Build();
            using (provider)
            {
                for (var i = 0; i < 2; i++)
                {
                    await reader.GetMatchupsAsync("L1", 1, default);
                    await reader.GetRostersAsync("L1", default);
                    await reader.GetUsersAsync("L1", default);
                    await reader.GetLeagueAsync("L1", default);
                    await reader.GetStateAsync(default);
                    await reader.GetPlayersAsync(default);
                }

                socket.Attempts.Should().Be(6, "the second pass should be entirely cached");
            }
        }

        [Fact]
        public async Task One_caller_walking_away_does_not_cancel_the_others()
        {
            // The factory is handed its own token because a single fetch may be
            // shared. Capturing the caller's token instead would let whoever
            // gives up first cancel work everybody else is waiting on.
            var (reader, socket, provider) = Build(delay: TimeSpan.FromMilliseconds(100));
            using (provider)
            {
                using var abandoning = new CancellationTokenSource();

                var leaver = reader.GetMatchupsAsync("L1", 1, abandoning.Token).AsTask();
                var stayer = reader.GetMatchupsAsync("L1", 1, default).AsTask();

                await abandoning.CancelAsync();

                var act = async () => await stayer;
                await act.Should().NotThrowAsync("the remaining caller still needs the result");

                try { await leaver; } catch (OperationCanceledException) { }
                socket.Attempts.Should().Be(1);
            }
        }

        [Fact]
        public async Task A_large_payload_is_still_cached()
        {
            // HybridCache refuses to store anything above MaximumPayloadBytes,
            // one mebibyte by default, and does so quietly -- the call succeeds
            // and simply never caches. The real player dump is roughly 14 MB, so
            // if that limit applies here it would be re-fetched on every call.
            var players = "{" + string.Join(",", Enumerable.Range(0, 12000).Select(i =>
                $"\"{i}\":{{\"player_id\":\"{i}\",\"full_name\":\"Player Number {i}\"," +
                $"\"team\":\"KC\",\"position\":\"QB\",\"fantasy_positions\":[\"QB\"]}}")) + "}";

            players.Length.Should().BeGreaterThan(1024 * 1024, "the fixture must exceed the default cap");

            var (reader, socket, provider) = Build(players);
            using (provider)
            {
                await reader.GetPlayersAsync(default);
                await reader.GetPlayersAsync(default);

                socket.Attempts.Should().Be(1);
            }
        }
    }
}
