using System.Net;
using System.Text;
using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Records every request it is handed and answers with a canned body, so the
    /// client can be exercised without a network.
    /// </summary>
    internal sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult((respond ?? Default)(request));
        }

        /// <summary>Answers with the smallest body that binds for the path asked for.</summary>
        private static HttpResponseMessage Default(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body =
                path.EndsWith("state/nfl", StringComparison.Ordinal) ? """{"season":"2026","week":1}""" :
                path.EndsWith("players/nfl", StringComparison.Ordinal) ? "{}" :
                path.Contains("/users", StringComparison.Ordinal) ||
                path.Contains("/rosters", StringComparison.Ordinal) ||
                path.Contains("/matchups/", StringComparison.Ordinal) ? "[]" :
                """{"league_id":"L1","season":"2026","roster_positions":["QB","BN"]}""";

            return Json(HttpStatusCode.OK, body);
        }

        public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    /// <summary>
    /// The client's request construction, which is the part a stray slash breaks
    /// silently.
    /// </summary>
    public class SleeperClientUrlTests
    {
        private const string BaseAddress = "https://api.sleeper.app/v1/";

        private static (SleeperClient Client, RecordingHandler Handler) Build()
        {
            var handler = new RecordingHandler();
            var http = new HttpClient(handler) { BaseAddress = new Uri(BaseAddress) };
            return (new SleeperClient(http), handler);
        }

        private static Task Invoke(SleeperClient client, string method) => method switch
        {
            "state" => client.GetNFLStateAsync(),
            "league" => client.GetLeagueAsync("L1"),
            "users" => client.GetUsersAsync("L1"),
            "rosters" => client.GetRostersAsync("L1"),
            "matchups" => client.GetMatchupsAsync("L1", 7),
            "players" => client.GetPlayersAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };

        [Theory]
        [InlineData("state", "https://api.sleeper.app/v1/state/nfl")]
        [InlineData("league", "https://api.sleeper.app/v1/league/L1")]
        [InlineData("users", "https://api.sleeper.app/v1/league/L1/users")]
        [InlineData("rosters", "https://api.sleeper.app/v1/league/L1/rosters")]
        [InlineData("matchups", "https://api.sleeper.app/v1/league/L1/matchups/7")]
        [InlineData("players", "https://api.sleeper.app/v1/players/nfl")]
        public async Task Each_method_requests_its_documented_url(string method, string expected)
        {
            // The base address ends with a slash and every relative path begins
            // without one. Swap either and Uri resolution quietly drops the /v1
            // segment, which fails as a 404 rather than as anything that names
            // the cause.
            var (client, handler) = Build();

            await Invoke(client, method);

            handler.Requests.Should().ContainSingle();
            handler.Requests[0].RequestUri!.ToString().Should().Be(expected);
        }

        [Theory]
        [InlineData("state")]
        [InlineData("league")]
        [InlineData("players")]
        public async Task Every_call_is_a_get(string method)
        {
            var (client, handler) = Build();

            await Invoke(client, method);

            handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        }

        [Fact]
        public async Task The_week_number_is_not_url_encoded_into_something_else()
        {
            var (client, handler) = Build();

            await client.GetMatchupsAsync("L1", 18, TestContext.Current.CancellationToken);

            handler.Requests[0].RequestUri!.AbsolutePath.Should().EndWith("/matchups/18");
        }
    }

    /// <summary>
    /// What the client does with the response it gets back.
    /// </summary>
    public class SleeperClientResponseTests
    {
        private static SleeperClient ClientReturning(HttpStatusCode status, string body, out RecordingHandler handler)
        {
            handler = new RecordingHandler(_ => RecordingHandler.Json(status, body));
            var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.sleeper.app/v1/") };
            return new SleeperClient(http);
        }

        [Fact]
        public async Task The_shared_serializer_options_are_applied()
        {
            // season arrives as the string "2026". Without SleeperJson.Options
            // reaching this call, binding it to an int throws.
            var client = ClientReturning(
                HttpStatusCode.OK,
                """{"season":"2026","season_type":"regular","week":3,"display_week":3}""",
                out _);

            var state = await client.GetNFLStateAsync(TestContext.Current.CancellationToken);

            state!.Season.Should().Be(2026);
            state.Week.Should().Be(3);
        }

        [Fact]
        public async Task An_out_of_range_week_comes_back_empty_rather_than_null()
        {
            // Sleeper answers a week beyond the season with [], so the caller gets
            // an empty list. A week that merely has not been played yet is a
            // different thing entirely and returns a full payload of zeroes.
            var client = ClientReturning(HttpStatusCode.OK, "[]", out _);

            var matchups = await client.GetMatchupsAsync("L1", 99, TestContext.Current.CancellationToken);

            matchups.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public async Task A_future_week_is_indistinguishable_from_a_week_of_zeroes()
        {
            // Week 18 really does return points 0.0 with every field present, so
            // nothing in the payload says whether a week has happened. That has to
            // come from /state/nfl -- there is no "no points at all" to detect.
            var client = ClientReturning(
                HttpStatusCode.OK,
                """
                [{"roster_id":1,"matchup_id":1,"points":0.0,"custom_points":null,
                  "players":["1"],"starters":["1"],"starters_points":[0.0],
                  "players_points":{"1":0.0}}]
                """,
                out _);

            var matchups = await client.GetMatchupsAsync("L1", 18, TestContext.Current.CancellationToken);

            matchups.Should().ContainSingle();
            matchups![0]!.Points.Should().Be(0m);
            matchups[0]!.StartersPoints.Should().NotBeNull().And.OnlyContain(p => p == 0m);
        }

        [Fact]
        public async Task The_player_dump_binds_as_a_map_not_a_list()
        {
            // /v1/players/nfl is a JSON object keyed by player id.
            var client = ClientReturning(
                HttpStatusCode.OK,
                """{"4046":{"player_id":"4046","full_name":"Patrick Mahomes","team":"KC","fantasy_positions":["QB"]}}""",
                out _);

            var dump = await client.GetPlayersAsync(TestContext.Current.CancellationToken);

            dump.Should().ContainKey("4046");
            dump!["4046"]!.FantasyPositions.Should().Equal("QB");
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        public async Task A_non_success_status_throws(HttpStatusCode status)
        {
            // GetFromJsonAsync calls EnsureSuccessStatusCode, so failures surface
            // as HttpRequestException rather than as a null result. T12's
            // resilience policy is what decides which of these are retried.
            var client = ClientReturning(status, "{}", out _);

            await FluentActions.Awaiting(() => client.GetNFLStateAsync())
                .Should().ThrowAsync<HttpRequestException>();
        }

        [Fact]
        public async Task A_cancelled_token_stops_the_request()
        {
            var client = ClientReturning(HttpStatusCode.OK, "{}", out _);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await FluentActions.Awaiting(() => client.GetNFLStateAsync(cts.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }
    }

    /// <summary>
    /// The registration, which is where the base address actually gets set in
    /// production.
    /// </summary>
    public class SleeperClientRegistrationTests
    {
        private static ServiceProvider Provider()
        {
            var services = new ServiceCollection();
            services.AddSleeperClient();
            return services.BuildServiceProvider();
        }

        [Fact]
        public void The_client_can_be_resolved()
        {
            using var provider = Provider();

            provider.GetRequiredService<SleeperClient>().Should().NotBeNull();
        }

        [Fact]
        public void The_base_address_keeps_its_trailing_slash()
        {
            // Without the trailing slash, Uri resolution against a relative path
            // replaces the last segment instead of appending to it, and every
            // request silently loses /v1.
            using var provider = Provider();
            var http = provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(nameof(SleeperClient));

            http.BaseAddress.Should().Be(new Uri("https://api.sleeper.app/v1/"));
            http.BaseAddress!.ToString().Should().EndWith("/");
        }

        [Fact]
        public void The_client_identifies_itself()
        {
            // Sleeper's API is unauthenticated, so a user agent is the only way a
            // request from this site is attributable if it ever needs to be.
            using var provider = Provider();
            var http = provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(nameof(SleeperClient));

            http.DefaultRequestHeaders.UserAgent.ToString().Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void The_pipeline_owns_the_timeout_not_the_client()
        {
            // AddStandardResilienceHandler forces HttpClient.Timeout to infinite,
            // and overrides any value configured on the client. That is deliberate:
            // HttpClient.Timeout covers the whole operation including retries, so
            // leaving it finite would cut the pipeline off part way through and
            // surface as a bare TaskCanceledException naming no cause.
            //
            // The real budget is the pipeline's TotalRequestTimeout, 30s by
            // default. Setting c.Timeout in AddSleeperClient would be silently
            // ignored, so this asserts the arrangement rather than a number.
            using var provider = Provider();
            var http = provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(nameof(SleeperClient));

            http.Timeout.Should().Be(Timeout.InfiniteTimeSpan,
                "the resilience pipeline bounds the operation instead");
        }

        [Fact]
        public void Registration_returns_the_builder_so_a_host_can_add_more()
        {
            // The return value is the pipeline handle. Dropping it is how the
            // rate limit handler ended up registered but never attached.
            var services = new ServiceCollection();

            services.AddSleeperClient().Should().BeAssignableTo<IHttpClientBuilder>();
        }

    }
}
