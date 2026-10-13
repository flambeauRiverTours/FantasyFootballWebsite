using System.Net;
using System.Text;
using System.Text.Json;
using FantasyLeague.Seasons;
using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Serves a body per Sleeper path, so a whole season can be assembled offline.
    /// </summary>
    internal sealed class LeagueSocket : HttpMessageHandler
    {
        public string State { get; set; } = Bodies.State(week: 2);
        public string League { get; set; } = Bodies.League();
        public string Users { get; set; } = Bodies.Users();
        public string Rosters { get; set; } = Bodies.Rosters();
        public Dictionary<int, string> Matchups { get; } = [];
        public string DefaultMatchups { get; set; } = "[]";
        public string Players { get; set; } = Bodies.Players();

        /// <summary>League bodies by league id, for a chain of seasons; any other id gets <see cref="League"/>.</summary>
        public Dictionary<string, string> Leagues { get; } = [];

        public int MatchupRequests { get; private set; }

        /// <summary>
        /// The real league as captured: every <c>wk*-matchups.json</c> verbatim,
        /// the latest served as the week in progress, the league's own slots and
        /// a player dump built from the captured positions. Users and rosters are
        /// invented, because a capture of them is personal data (A2).
        /// </summary>
        /// <returns>A socket serving the captured season.</returns>
        public static LeagueSocket Captured()
        {
            var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
            var weeks = Directory.GetFiles(fixtures, "wk*-matchups.json")
                .ToDictionary(
                    path => int.Parse(Path.GetFileName(path)[2..^"-matchups.json".Length]),
                    File.ReadAllText);

            using var slots = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "roster-positions.json")));
            using var positions = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "player-positions.json")));
            var dump = positions.RootElement.EnumerateObject().ToDictionary(
                p => p.Name,
                p => new
                {
                    player_id = p.Name,
                    full_name = p.Value.GetProperty("name").GetString(),
                    team = p.Value.GetProperty("team").GetString(),
                    fantasy_positions = p.Value.GetProperty("fantasyPositions"),
                });

            var rosterIds = Enumerable.Range(1, 6).ToList();
            var socket = new LeagueSocket
            {
                State = Bodies.State(week: weeks.Keys.Max()),
                League = Bodies.League([.. slots.RootElement.GetProperty("roster_positions").EnumerateArray().Select(e => e.GetString()!)]),
                Users = JsonSerializer.Serialize(rosterIds.Select(i => new { user_id = $"u{i}", display_name = $"manager{i}", metadata = new { team_name = $"Team {i}" } })),
                Rosters = JsonSerializer.Serialize(rosterIds.Select(i => new { roster_id = i, owner_id = $"u{i}" })),
                Players = JsonSerializer.Serialize(dump),
            };

            foreach (var (week, body) in weeks)
            {
                socket.Matchups[week] = body;
            }

            return socket;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = BodyFor(path);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }

        private string BodyFor(string path)
        {
            if (path.EndsWith("state/nfl", StringComparison.Ordinal))
            {
                return State;
            }

            if (path.EndsWith("players/nfl", StringComparison.Ordinal))
            {
                return Players;
            }

            if (path.EndsWith("/users", StringComparison.Ordinal))
            {
                return Users;
            }

            if (path.EndsWith("/rosters", StringComparison.Ordinal))
            {
                return Rosters;
            }

            var marker = path.LastIndexOf("/matchups/", StringComparison.Ordinal);
            if (marker >= 0)
            {
                MatchupRequests++;
                var week = int.Parse(path[(marker + "/matchups/".Length)..]);
                return Matchups.TryGetValue(week, out var scripted) ? scripted : DefaultMatchups;
            }

            return Leagues.TryGetValue(path[(path.LastIndexOf('/') + 1)..], out var league) ? league : League;
        }
    }

    internal static class Bodies
    {
        public static string State(int week) =>
            $$"""{"season":"2026","season_type":"regular","week":{{week}},"display_week":{{week}}}""";

        public static string League(params string[] rosterPositions)
        {
            var slots = rosterPositions.Length > 0 ? rosterPositions : ["QB", "RB", "FLEX", "BN"];
            var json = JsonSerializer.Serialize(slots);
            return $$"""{"league_id":"L1","previous_league_id":null,"season":"2026","name":"Test","status":"in_season","total_rosters":2,"roster_positions":{{json}}}""";
        }

        public static string League(int season, string? previousLeagueId, string name = "Test")
        {
            var previous = previousLeagueId is null ? "null" : $"\"{previousLeagueId}\"";
            return $$"""{"previous_league_id":{{previous}},"season":"{{season}}","name":"{{name}}","roster_positions":["QB","RB","FLEX","BN"]}""";
        }

        public static string Users() =>
            """
            [{"user_id":"u1","display_name":"jtreads","is_bot":false,"metadata":{"team_name":"START TOMMY CUTLETS"}},
             {"user_id":"u2","display_name":"kuzma4prez","is_bot":false,"metadata":{}}]
            """;

        public static string Rosters() =>
            """[{"roster_id":1,"owner_id":"u1","co_owners":null},{"roster_id":2,"owner_id":"u2","co_owners":null}]""";

        public static string Players() =>
            """
            {
              "qb1": {"player_id":"qb1","full_name":"Quarter Back","team":"KC","position":"QB","fantasy_positions":["QB"]},
              "rb1": {"player_id":"rb1","full_name":"Running Back","team":"SF","position":"RB","fantasy_positions":["RB"]},
              "wr1": {"player_id":"wr1","full_name":"Wide Out","team":"BUF","position":"WR","fantasy_positions":["WR"]}
            }
            """;

        public static string Matchup(
            int rosterId,
            decimal points,
            decimal? customPoints = null,
            string[]? players = null,
            string[]? starters = null,
            decimal[]? startersPoints = null,
            Dictionary<string, decimal>? playersPoints = null)
        {
            players ??= ["qb1", "rb1", "wr1"];
            starters ??= ["qb1", "rb1", "wr1"];
            startersPoints ??= [20m, 10m, 5m];
            playersPoints ??= new Dictionary<string, decimal> { ["qb1"] = 20m, ["rb1"] = 10m, ["wr1"] = 5m };

            var custom = customPoints is null ? "null" : customPoints.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return $$"""
                {"roster_id":{{rosterId}},"matchup_id":1,
                 "points":{{points.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                 "custom_points":{{custom}},
                 "players":{{JsonSerializer.Serialize(players)}},
                 "starters":{{JsonSerializer.Serialize(starters)}},
                 "starters_points":{{JsonSerializer.Serialize(startersPoints)}},
                 "players_points":{{JsonSerializer.Serialize(playersPoints)}}}
                """;
        }

        public static string Week(params string[] matchups) => "[" + string.Join(",", matchups) + "]";
    }

    public class SeasonServiceTests
    {
        private static async Task<(ISeasonService Service, ServiceProvider Provider)> BuildAsync(LeagueSocket socket)
        {
            var services = TestServices.Create();
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                    .ConfigurePrimaryHttpMessageHandler(() => socket);
            services.AddSeasonServiceWithNullStores();

            var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IPlayerMapRefresher>().RefreshAsync(default);

            return (provider.GetRequiredService<ISeasonService>(), provider);
        }

        private static LeagueSocket TwoTeamsTwoWeeks()
        {
            var socket = new LeagueSocket { State = Bodies.State(week: 2) };
            socket.Matchups[1] = Bodies.Week(Bodies.Matchup(1, 35m), Bodies.Matchup(2, 35m));
            socket.Matchups[2] = Bodies.Week(Bodies.Matchup(1, 35m), Bodies.Matchup(2, 35m));
            return socket;
        }

        [Fact]
        public async Task The_player_map_is_loaded_without_an_explicit_refresh()
        {
            // the deployed endpoint had nothing calling the refresher, and every request 500'd
            var services = TestServices.Create();
            services.AddSleeperClient().ConfigurePrimaryHttpMessageHandler(LeagueSocket.Captured);
            services.AddSeasonServiceWithNullStores();
            using var provider = services.BuildServiceProvider();

            var snapshot = await provider.GetRequiredService<ISeasonService>().BuildAsync(TestContext.Current.CancellationToken);

            snapshot.Teams.Should().NotBeEmpty();
            snapshot.UnresolvedPlayerIds.Should().BeEmpty("every player was found in the loaded map");
        }

        [Fact]
        public async Task A_dump_that_cannot_be_read_is_still_refused()
        {
            // A8: an empty map would report every player unresolved instead of failing
            var services = TestServices.Create();
            services.AddSleeperClient()
                .ConfigurePrimaryHttpMessageHandler(() => new LeagueSocket { Players = "null" });
            services.AddSeasonServiceWithNullStores();
            using var provider = services.BuildServiceProvider();

            await FluentActions.Awaiting(() => provider.GetRequiredService<ISeasonService>().BuildAsync(default))
                .Should().ThrowAsync<InvalidDataException>();
        }

        [Fact]
        public async Task Every_roster_becomes_one_team_with_its_weeks_in_order()
        {
            var (service, provider) = await BuildAsync(LeagueSocket.Captured());
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);
                var weeks = Enumerable.Range(1, snapshot.CurrentWeek);

                snapshot.Teams.Select(t => t.RosterId).Should().Equal(1, 2, 3, 4, 5, 6);
                snapshot.Teams.Should().OnlyContain(t => t.Weeks.Select(w => w.Week).SequenceEqual(weeks));
            }
        }

        [Fact]
        public async Task A_captured_season_assembles_with_every_player_known_and_no_breach()
        {
            // the whole read path over what Sleeper actually sent, not over shapes we invented
            var (service, provider) = await BuildAsync(LeagueSocket.Captured());
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);
                var weeks = snapshot.Teams.SelectMany(t => t.Weeks).ToList();

                snapshot.UnresolvedPlayerIds.Should().BeEmpty();
                snapshot.Violations.Should().BeEmpty();
                weeks.Should().OnlyContain(w => w.IdealPts >= w.ActualPts);
                weeks.Should().OnlyContain(w => w.SlotAverages.Count > 0, "every captured lineup lines up with its slots");
            }
        }

        [Fact]
        public async Task Team_names_come_from_the_users_endpoint()
        {
            var (service, provider) = await BuildAsync(TwoTeamsTwoWeeks());
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.Teams[0].Name.Should().Be("START TOMMY CUTLETS");
                snapshot.Teams[1].Name.Should().Be("kuzma4prez");
            }
        }

        [Fact]
        public async Task Final_through_week_stops_before_the_week_in_progress()
        {
            var (service, provider) = await BuildAsync(LeagueSocket.Captured());
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.FinalThroughWeek.Should().Be(snapshot.CurrentWeek - 1);
            }
        }

        [Fact]
        public async Task In_week_one_nothing_is_final_yet()
        {
            var socket = new LeagueSocket { State = Bodies.State(week: 1) };
            socket.Matchups[1] = Bodies.Week(Bodies.Matchup(1, 35m), Bodies.Matchup(2, 35m));

            var (service, provider) = await BuildAsync(socket);
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.FinalThroughWeek.Should().Be(0);
                snapshot.Teams.SelectMany(t => t.Weeks).Should().OnlyContain(w => !w.IsFinal);
            }
        }

        [Fact]
        public async Task Only_completed_weeks_are_marked_final()
        {
            var (service, provider) = await BuildAsync(LeagueSocket.Captured());
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.Teams.SelectMany(t => t.Weeks).Should()
                    .OnlyContain(w => w.IsFinal == (w.Week < snapshot.CurrentWeek));
            }
        }

        [Fact]
        public async Task Requests_inside_the_cache_window_share_one_build()
        {
            // one build per window is what keeps its error logs to one per window
            var (service, provider) = await BuildAsync(LeagueSocket.Captured());
            using (provider)
            {
                var first = await service.BuildAsync(TestContext.Current.CancellationToken);
                var second = await service.BuildAsync(TestContext.Current.CancellationToken);

                second.Should().BeSameAs(first);
            }
        }

        [Fact]
        public async Task The_current_season_is_read_without_building_one()
        {
            var socket = TwoTeamsTwoWeeks();
            socket.DefaultMatchups = "not json";
            var (service, provider) = await BuildAsync(socket);
            using (provider)
            {
                (await service.GetCurrentSeasonAsync(TestContext.Current.CancellationToken)).Should().Be(2026);
            }
        }

        [Fact]
        public async Task The_slots_come_from_this_season_roster_positions()
        {
            var socket = TwoTeamsTwoWeeks();
            socket.League = Bodies.League("QB", "RB", "WR", "FLEX", "BN", "BN");

            var (service, provider) = await BuildAsync(socket);
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.Slots.Select(s => s.Code).Should().Equal("QB", "RB", "WR", "FLEX");
            }
        }

        [Fact]
        public async Task Ideal_beats_actual_when_the_wrong_player_was_benched()
        {
            var socket = new LeagueSocket { State = Bodies.State(week: 1) };
            socket.Matchups[1] = Bodies.Week(Bodies.Matchup(
                rosterId: 1,
                points: 12m,
                players: ["qb1", "rb1"],
                starters: ["qb1"],
                startersPoints: [12m],
                playersPoints: new Dictionary<string, decimal> { ["qb1"] = 12m, ["rb1"] = 30m }));

            var (service, provider) = await BuildAsync(socket);
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);
                var week = snapshot.Teams[0].Weeks[0];

                week.ActualPts.Should().Be(12m);
                week.IdealPts.Should().Be(42m);
                snapshot.Violations.Should().BeEmpty();
            }
        }
    }

    /// <summary>
    /// Keeps every log entry at warning or above, for tests that count them.
    /// </summary>
    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturing(Entries);

        public void Dispose()
        {
        }

        private sealed class Capturing(List<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    lock (entries)
                    {
                        entries.Add(formatter(state, exception));
                    }
                }
            }
        }
    }

    public class SeasonServiceReportingTests
    {
        [Fact]
        public async Task A_problem_is_logged_once_however_often_the_season_is_rebuilt()
        {
            // a build every 5 s would otherwise repeat the same warning all Sunday
            var logs = new CapturingLoggerProvider();
            var services = TestServices.Create();
            services.AddLogging(b => b.AddProvider(logs));
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                    .ConfigurePrimaryHttpMessageHandler(() => OneWeek(Bodies.Matchup(
                        rosterId: 1,
                        points: 99m,
                        players: ["qb1", "rb1", "wr1", "ghost"])));
            services.AddSeasonServiceWithNullStores();
            using var provider = services.BuildServiceProvider();

            for (var build = 0; build < 3; build++)
            {
                await provider.GetRequiredService<HybridCache>().RemoveAsync("season:L1", TestContext.Current.CancellationToken);
                await provider.GetRequiredService<ISeasonService>().BuildAsync(TestContext.Current.CancellationToken);
            }

            logs.Entries.Should().ContainSingle(e => e.Contains("ghost"));
            logs.Entries.Where(e => e.StartsWith("Invariant", StringComparison.Ordinal))
                .Should().HaveCount(2, "two invariants fail, each logged once");
        }

        private static async Task<(ISeasonService Service, ServiceProvider Provider)> BuildAsync(LeagueSocket socket)
        {
            var services = TestServices.Create();
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                    .ConfigurePrimaryHttpMessageHandler(() => socket);
            services.AddSeasonServiceWithNullStores();

            var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IPlayerMapRefresher>().RefreshAsync(default);

            return (provider.GetRequiredService<ISeasonService>(), provider);
        }

        private static LeagueSocket OneWeek(string matchup)
        {
            var socket = new LeagueSocket { State = Bodies.State(week: 1) };
            socket.Matchups[1] = Bodies.Week(matchup);
            return socket;
        }

        [Fact]
        public async Task An_unknown_player_is_reported()
        {
            var (service, provider) = await BuildAsync(OneWeek(Bodies.Matchup(
                rosterId: 1,
                points: 30m,
                players: ["qb1", "rb1", "signed_tuesday"],
                starters: ["qb1", "rb1"],
                startersPoints: [20m, 10m],
                playersPoints: new Dictionary<string, decimal> { ["qb1"] = 20m, ["rb1"] = 10m })));

            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.UnresolvedPlayerIds.Should().Equal("signed_tuesday");
            }
        }

        [Fact]
        public async Task An_unknown_player_is_reported_once_across_weeks()
        {
            var socket = new LeagueSocket { State = Bodies.State(week: 3) };
            var matchup = Bodies.Matchup(
                rosterId: 1,
                points: 20m,
                players: ["qb1", "signed_tuesday"],
                starters: ["qb1"],
                startersPoints: [20m],
                playersPoints: new Dictionary<string, decimal> { ["qb1"] = 20m });
            socket.Matchups[1] = Bodies.Week(matchup);
            socket.Matchups[2] = Bodies.Week(matchup);
            socket.Matchups[3] = Bodies.Week(matchup);

            var (service, provider) = await BuildAsync(socket);
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.UnresolvedPlayerIds.Should().Equal("signed_tuesday");
            }
        }

        [Fact]
        public async Task A_clean_week_reports_nothing()
        {
            var (service, provider) = await BuildAsync(OneWeek(Bodies.Matchup(1, 35m)));
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.Violations.Should().BeEmpty();
                snapshot.UnresolvedPlayerIds.Should().BeEmpty();
            }
        }

        [Fact]
        public async Task A_starters_sum_below_the_points_is_caught()
        {
            var (service, provider) = await BuildAsync(OneWeek(Bodies.Matchup(
                rosterId: 1, points: 35m, startersPoints: [20m, 10m, 1m])));

            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.Violations.Should().ContainSingle()
                    .Which.Invariant.Should().Be(InvariantKind.StartersSumMatchesPoints);
            }
        }

        [Fact]
        public async Task A_starters_sum_above_the_points_is_caught_too()
        {
            var (service, provider) = await BuildAsync(OneWeek(Bodies.Matchup(
                rosterId: 1, points: 30m, startersPoints: [20m, 10m, 5m])));

            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                var violation = snapshot.Violations.Should().ContainSingle().Subject;
                violation.Invariant.Should().Be(InvariantKind.StartersSumMatchesPoints);
                violation.SleeperPts.Should().Be(30m);
                violation.ComparedPts.Should().Be(35m);
            }
        }

        [Fact]
        public async Task A_cent_of_drift_is_tolerated()
        {
            var (service, provider) = await BuildAsync(OneWeek(Bodies.Matchup(
                rosterId: 1, points: 35.01m, startersPoints: [20m, 10m, 5m])));

            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.Violations.Should().BeEmpty();
            }
        }

        [Fact]
        public async Task A_commissioner_override_does_not_look_like_a_broken_invariant()
        {
            var (service, provider) = await BuildAsync(OneWeek(Bodies.Matchup(
                rosterId: 1, points: 35m, customPoints: 41m)));

            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.Violations.Should().BeEmpty();
                snapshot.Teams[0].Weeks[0].ActualPts.Should().Be(41m);
            }
        }

        [Fact]
        public async Task An_override_beyond_the_ideal_is_not_a_solver_bug()
        {
            var (service, provider) = await BuildAsync(OneWeek(Bodies.Matchup(
                rosterId: 1, points: 35m, customPoints: 500m)));

            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.Violations.Should().BeEmpty();
                snapshot.Teams[0].Weeks[0].ActualPts.Should().Be(500m);
                snapshot.Teams[0].Weeks[0].IdealPts.Should().Be(35m);
            }
        }

        [Fact]
        public async Task A_genuinely_impossible_score_is_still_reported()
        {
            var (service, provider) = await BuildAsync(OneWeek(Bodies.Matchup(
                rosterId: 1, points: 500m, startersPoints: [500m, 0m, 0m])));

            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.Violations.Should().ContainSingle()
                    .Which.Invariant.Should().Be(InvariantKind.IdealNotBelowActual);
            }
        }

        [Fact]
        public async Task A_violation_names_its_week_and_roster()
        {
            var socket = new LeagueSocket { State = Bodies.State(week: 2) };
            socket.Matchups[1] = Bodies.Week(Bodies.Matchup(1, 35m));
            socket.Matchups[2] = Bodies.Week(Bodies.Matchup(7, 35m, startersPoints: [20m, 10m, 1m]));

            var (service, provider) = await BuildAsync(socket);
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                var violation = snapshot.Violations.Should().ContainSingle().Subject;
                violation.Week.Should().Be(2);
                violation.RosterId.Should().Be(7);
            }
        }

        [Fact]
        public async Task A_roster_with_no_matching_user_still_gets_a_name()
        {
            var (service, provider) = await BuildAsync(OneWeek(Bodies.Matchup(9, 35m)));
            using (provider)
            {
                var snapshot = await service.BuildAsync(TestContext.Current.CancellationToken);

                snapshot.Teams[0].Name.Should().Be("Roster 9");
            }
        }
    }
}
