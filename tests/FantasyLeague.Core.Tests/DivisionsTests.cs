using System.Text.Json;
using FantasyLeague.Cli;
using FantasyLeague.Seasons;
using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Division standings (T72b, A48): read from Sleeper, grouped in head-to-head only (Q14),
    /// and flat whenever a roster cannot be placed. No league with divisions has been observed,
    /// so every body here is invented, its names placeholders.
    /// </summary>
    public class DivisionsTests
    {
        private static readonly IReadOnlyList<Division> TwoDivisions = [new(1, "North"), new(2, "South")];

        private static IReadOnlyList<Division>? Divisions(string body) =>
            SleeperMapper.ToDivisions(Fixtures.Read<LeagueDto>(body));

        private static TeamSeason InGames(string name, int rosterId, int? division, params (decimal Actual, int MatchupId)[] weeks) =>
            new(rosterId, name,
                [.. weeks.Select((w, i) => new TeamWeek(rosterId, i + 1, w.Actual, w.Actual, [], [], [], true, new Dictionary<string, decimal>(), w.MatchupId))],
                division);

        // two settled weeks. North: Alpha 2-0, Bravo 0-2. South: Delta and Charlie 1-1, Delta ahead on
        // points for. League-wide the South's leader is a game behind Alpha; in its division it is not
        private static SeasonSnapshot Season(
            LeagueFormat format = LeagueFormat.HeadToHead,
            IReadOnlyList<Division>? divisions = null,
            int? bravosDivision = 1) =>
            new("Name", 2026, 3, 2, Slot.Parse(["QB", "RB", "FLEX"]),
                [
                    InGames("Alpha", 1, 1, (100m, 1), (90m, 1)),
                    InGames("Bravo", 2, bravosDivision, (10m, 1), (20m, 2)),
                    InGames("Charlie", 3, 2, (60m, 2), (30m, 1)),
                    InGames("Delta", 4, 2, (50m, 2), (45m, 2)),
                ],
                [], [], IsCurrent: true, RegularSeason.NFLWeeks, format, [], false, null,
                Divisions: divisions ?? TwoDivisions);

        // ---------- reading Sleeper

        [Theory]
        [InlineData("""{"season":"2026","settings":{"divisions":2},"metadata":{"division_1":"North","division_2":" South "}}""", "North|South")]
        [InlineData("""{"season":"2026","settings":{"divisions":2},"metadata":{"division_1":"North"}}""", "North|Division 2")]
        [InlineData("""{"season":"2026","settings":{"divisions":2},"metadata":{"division_1":"  ","division_2":"South"}}""", "Division 1|South")]
        [InlineData("""{"season":"2026","settings":{"divisions":2}}""", "Division 1|Division 2")]
        // metadata is free-form commissioner and app data: an odd value, or no object at all, fails nothing
        [InlineData("""{"season":"2026","settings":{"divisions":2},"metadata":{"division_1":3,"division_2":{"name":"South"},"division_1_avatar":"https://example.invalid/a.png"}}""", "Division 1|Division 2")]
        [InlineData("""{"season":"2026","settings":{"divisions":2},"metadata":""}""", "Division 1|Division 2")]
        public void Divisions_are_named_from_the_metadata_or_numbered(string body, string expected) =>
            Divisions(body)!.Select(d => d.Name).Should().Equal(expected.Split('|'));

        [Theory]
        [InlineData("""{"season":"2026","settings":{"divisions":0}}""")]
        [InlineData("""{"season":"2026","settings":{"divisions":1},"metadata":{"division_1":"Only"}}""")]
        [InlineData("""{"season":"2026","settings":{}}""")]
        [InlineData("""{"season":"2026"}""")]
        public void Fewer_than_two_divisions_is_none(string body) =>
            Divisions(body).Should().BeNull();

        [Fact]
        public void The_captured_league_has_no_divisions() =>
            Divisions(Fixtures.Text("league-settings.json")).Should().BeNull();

        [Fact]
        public void A_roster_s_division_is_read_from_its_settings()
        {
            var rosters = new List<RosterDto>
            {
                new(1, null, new RosterSettingsDto(null, null, null, null, null, null, null, Division: 2)),
                new(2, null, null),
            };

            var teams = SleeperMapper.ToTeams([], rosters);

            teams[1].Division.Should().Be(2);
            teams[2].Division.Should().BeNull();
        }

        // ---------- when the standings are grouped

        [Fact]
        public void Head_to_head_with_every_roster_placed_is_grouped()
        {
            var snapshot = Season();

            snapshot.ShownDivisions.Should().Equal(TwoDivisions);
            snapshot.UnplacedRosters.Should().BeEmpty();
        }

        [Fact]
        public void Total_points_is_never_grouped() =>
            // Q14: divisions are head-to-head only
            Season(LeagueFormat.TotalPoints).ShownDivisions.Should().BeNull();

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        [InlineData(3)]
        public void One_roster_out_of_place_shows_the_whole_season_flat(int? division)
        {
            // a half-grouped table misleads
            var snapshot = Season(bravosDivision: division);

            snapshot.ShownDivisions.Should().BeNull();
            snapshot.UnplacedRosters.Should().Equal(2);
        }

        [Fact]
        public void A_division_with_no_counted_roster_is_left_out()
        {
            // its only member excluded (A1), or a division the commissioner left empty
            var snapshot = Season(divisions: [.. TwoDivisions, new Division(3, "Empty")]);

            snapshot.ShownDivisions!.Select(d => d.Number).Should().Equal(1, 2);
        }

        // ---------- the payload

        [Fact]
        public void The_payload_carries_the_divisions_and_each_team_s_division()
        {
            var payload = SeasonPayloadMapper.ToSeasonPayload(Season(), DateTimeOffset.UnixEpoch);

            payload.Divisions.Should().Equal(new DivisionPayload(1, "North"), new DivisionPayload(2, "South"));
            payload.Teams.ToDictionary(t => t.Name, t => t.Division).Should().BeEquivalentTo(
                new Dictionary<string, int?> { ["Alpha"] = 1, ["Bravo"] = 1, ["Charlie"] = 2, ["Delta"] = 2 });
        }

        [Theory]
        [InlineData(LeagueFormat.TotalPoints, 1)]
        [InlineData(LeagueFormat.HeadToHead, null)]
        public void Ungrouped_the_body_has_no_division_field_at_all(LeagueFormat format, int? bravosDivision)
        {
            // omitted rather than null, so no existing body changes and no ETag moves
            var payload = SeasonPayloadMapper.ToSeasonPayload(Season(format, bravosDivision: bravosDivision), DateTimeOffset.UnixEpoch);

            JsonSerializer.Serialize(payload).Should().NotContain("ivision");
        }

        // ---------- the CLI

        [Fact]
        public void The_cli_heads_each_division_and_measures_games_back_within_it()
        {
            var writer = new StringWriter();
            var snapshot = Season();
            StandingsRenderer.Render(SeasonPayloadMapper.ToSeasonPayload(snapshot, DateTimeOffset.UnixEpoch), snapshot, writer);
            var lines = writer.ToString().Split(Environment.NewLine);

            var north = Array.IndexOf(lines, "  North");
            var south = Array.IndexOf(lines, "  South");
            north.Should().BeLessThan(south);
            static string Gb(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[2];
            lines[north + 1].Should().StartWith("Alpha ");
            lines[north + 2].Should().StartWith("Bravo ");
            Gb(lines[north + 2]).Should().Be("2.0");
            // the South's 1-1 teams trail Alpha league-wide, but lead their own division
            lines[south + 1].Should().StartWith("Delta ");
            lines[south + 2].Should().StartWith("Charlie ");
            Gb(lines[south + 1]).Should().Be("-");
            Gb(lines[south + 2]).Should().Be("-");
        }

        // ---------- end to end, through Sleeper's bodies

        [Fact]
        public async Task A_league_with_divisions_is_read_per_roster_and_an_unplaced_roster_is_logged_once()
        {
            var logs = new CapturingLoggerProvider();
            var socket = new LeagueSocket
            {
                State = Bodies.State(week: 2),
                League = """{"season":"2026","name":"Test","roster_positions":["QB","RB","FLEX","BN"],"settings":{"divisions":2},"metadata":{"division_1":"North"}}""",
                Rosters = """[{"roster_id":1,"owner_id":"u1","settings":{"division":1}},{"roster_id":2,"owner_id":"u2","settings":{"division":2}}]""",
            };
            socket.Matchups[1] = Bodies.Week(Bodies.Matchup(1, 35m), Bodies.Matchup(2, 30m));
            var services = TestServices.Create(("League:Format", "HeadToHead"));
            services.AddLogging(b => b.AddProvider(logs));
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero).ConfigurePrimaryHttpMessageHandler(() => socket);
            services.AddSeasonServiceWithNullStores();
            using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IPlayerMapRefresher>().RefreshAsync(TestContext.Current.CancellationToken);
            var seasons = provider.GetRequiredService<ISeasonService>();
            var cache = provider.GetRequiredService<HybridCache>();

            var grouped = await seasons.BuildAsync(TestContext.Current.CancellationToken);

            grouped.ShownDivisions!.Select(d => d.Name).Should().Equal("North", "Division 2");
            grouped.Teams.Select(t => t.Division).Should().Equal(1, 2);

            socket.Rosters = """[{"roster_id":1,"owner_id":"u1","settings":{"division":1}},{"roster_id":2,"owner_id":"u2","settings":{}}]""";
            for (var build = 0; build < 3; build++)
            {
                await cache.RemoveAsync("rosters:L1", TestContext.Current.CancellationToken);
                await cache.RemoveAsync("season:L1", TestContext.Current.CancellationToken);
                (await seasons.BuildAsync(TestContext.Current.CancellationToken)).ShownDivisions.Should().BeNull();
            }

            logs.Entries.Should().ContainSingle(e => e.StartsWith("Roster 2 has no division", StringComparison.Ordinal));
        }
    }
}
