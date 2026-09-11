using System.Net;
using System.Text;
using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Answers with a canned body, or a status, per call.
    /// </summary>
    internal sealed class ScriptedSocket(params (HttpStatusCode Status, string Body)[] script) : HttpMessageHandler
    {
        private int _call;

        public int Attempts => Volatile.Read(ref _call);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // fresh response each call: HttpClient disposes content after reading
            var (status, body) = script[Math.Min(Interlocked.Increment(ref _call) - 1, script.Length - 1)];

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }

        public static (HttpStatusCode, string) Json(string body) => (HttpStatusCode.OK, body);

        public static (HttpStatusCode, string) Status(HttpStatusCode status) => (status, "{}");
    }

    public class PlayerMapRefresherTests
    {
        private static PlayerMapRefresher Build(ScriptedSocket socket)
        {
            var services = new ServiceCollection();
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                    .ConfigurePrimaryHttpMessageHandler(() => socket);

            var client = services.BuildServiceProvider().GetRequiredService<SleeperClient>();
            return new PlayerMapRefresher(client, NullLogger<PlayerMapRefresher>.Instance);
        }

        private const string Dump = """
            {
              "4046": {"player_id":"4046","full_name":"Patrick Mahomes","team":"KC","position":"QB","fantasy_positions":["QB"]},
              "1234": {"player_id":"1234","full_name":"Multi Eligible","team":"SF","position":"RB","fantasy_positions":["RB","WR"]},
              "BAL":  {"player_id":"BAL","full_name":null,"team":"BAL","position":"DEF","fantasy_positions":["DEF"]},
              "9999": {"player_id":"9999","full_name":"Long Snapper","team":"KC","position":"LS","fantasy_positions":[]},
              "8888": {"player_id":"8888","full_name":"No Positions","team":null,"position":null,"fantasy_positions":null}
            }
            """;

        [Fact]
        public async Task Before_the_first_refresh_the_map_is_empty_and_unstamped()
        {
            var refresher = Build(new ScriptedSocket(ScriptedSocket.Json(Dump)));

            refresher.PlayerMap.Should().BeEmpty();
            refresher.LastRefreshedUtc.Should().BeNull();

            await Task.CompletedTask;
        }

        [Fact]
        public async Task A_refresh_keeps_only_startable_players()
        {
            // nothing can be started at no position
            var refresher = Build(new ScriptedSocket(ScriptedSocket.Json(Dump)));

            await refresher.RefreshAsync(default);

            refresher.PlayerMap.Keys.Should().BeEquivalentTo(["4046", "1234", "BAL"]);
        }

        [Fact]
        public async Task A_player_without_a_name_falls_back_to_his_id()
        {
            var refresher = Build(new ScriptedSocket(ScriptedSocket.Json(Dump)));

            await refresher.RefreshAsync(default);

            refresher.PlayerMap["BAL"].Name.Should().Be("BAL");
            refresher.PlayerMap["BAL"].FantasyPositions.Should().Equal("DEF");
        }

        [Fact]
        public async Task Every_fantasy_position_survives_the_projection()
        {
            // eligibility matches on the plural field, so all of it has to arrive
            var refresher = Build(new ScriptedSocket(ScriptedSocket.Json(Dump)));

            await refresher.RefreshAsync(default);

            refresher.PlayerMap["1234"].FantasyPositions.Should().Equal("RB", "WR");
            refresher.PlayerMap["1234"].Team.Should().Be("SF");
        }

        [Fact]
        public async Task A_refresh_stamps_the_time()
        {
            var before = DateTimeOffset.UtcNow;
            var refresher = Build(new ScriptedSocket(ScriptedSocket.Json(Dump)));

            await refresher.RefreshAsync(default);

            refresher.LastRefreshedUtc.Should().NotBeNull().And.BeOnOrAfter(before);
        }

        [Fact]
        public async Task A_null_body_throws_rather_than_reporting_success()
        {
            // the timer invocation must go red, not silently keep yesterday's map
            var refresher = Build(new ScriptedSocket(ScriptedSocket.Json("null")));

            await FluentActions.Awaiting(() => refresher.RefreshAsync(default))
                .Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task A_failed_refresh_keeps_the_previous_map()
        {
            // stale player data beats none
            var socket = new ScriptedSocket(
                ScriptedSocket.Json(Dump),
                ScriptedSocket.Json("null"));
            var refresher = Build(socket);

            await refresher.RefreshAsync(default);
            var stamp = refresher.LastRefreshedUtc;

            await FluentActions.Awaiting(() => refresher.RefreshAsync(default))
                .Should().ThrowAsync<InvalidOperationException>();

            refresher.PlayerMap.Should().HaveCount(3);
            refresher.LastRefreshedUtc.Should().Be(stamp);
        }

        [Fact]
        public async Task An_http_failure_propagates_and_keeps_the_previous_map()
        {
            var socket = new ScriptedSocket(
                ScriptedSocket.Json(Dump),
                ScriptedSocket.Status(HttpStatusCode.InternalServerError));
            var refresher = Build(socket);

            await refresher.RefreshAsync(default);

            await FluentActions.Awaiting(() => refresher.RefreshAsync(default))
                .Should().ThrowAsync<HttpRequestException>();

            refresher.PlayerMap.Should().HaveCount(3);
        }

        [Fact]
        public async Task A_second_refresh_replaces_the_map_rather_than_merging()
        {
            var socket = new ScriptedSocket(
                ScriptedSocket.Json(Dump),
                ScriptedSocket.Json("""{"7777":{"player_id":"7777","full_name":"Only One","team":"KC","fantasy_positions":["TE"]}}"""));
            var refresher = Build(socket);

            await refresher.RefreshAsync(default);
            await refresher.RefreshAsync(default);

            refresher.PlayerMap.Keys.Should().BeEquivalentTo(["7777"]);
        }

        [Fact]
        public async Task A_refresh_bypasses_the_cache_and_really_fetches()
        {
            // T14: the dump is never served from a request-path cache
            var socket = new ScriptedSocket(ScriptedSocket.Json(Dump));
            var refresher = Build(socket);

            await refresher.RefreshAsync(default);
            await refresher.RefreshAsync(default);

            socket.Attempts.Should().Be(2);
        }

        [Fact]
        public async Task A_dump_the_size_of_the_real_one_projects()
        {
            // ~12k entries, well past HybridCache's 1 MiB default cap
            var body = "{" + string.Join(",", Enumerable.Range(0, 12000).Select(i =>
                $"\"{i}\":{{\"player_id\":\"{i}\",\"full_name\":\"Player Number {i}\"," +
                $"\"team\":\"KC\",\"position\":\"QB\",\"fantasy_positions\":[\"QB\"]}}")) + "}";
            body.Length.Should().BeGreaterThan(1024 * 1024);

            var refresher = Build(new ScriptedSocket(ScriptedSocket.Json(body)));

            await refresher.RefreshAsync(default);

            refresher.PlayerMap.Should().HaveCount(12000);
        }

        [Fact]
        public void The_refresher_is_a_singleton()
        {
            // the map it holds is shared state
            var services = new ServiceCollection();
            services.AddSleeperClient();
            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<IPlayerMapRefresher>()
                .Should().BeSameAs(provider.GetRequiredService<IPlayerMapRefresher>());
        }
    }
}
