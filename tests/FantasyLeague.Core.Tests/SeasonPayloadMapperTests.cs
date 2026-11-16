using System.Text.Json;
using FantasyLeague.Seasons;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    public class SeasonPayloadMapperTests
    {
        private static readonly DateTimeOffset AsOf = new(2026, 10, 18, 18, 42, 3, TimeSpan.Zero);

        private static readonly IReadOnlyDictionary<string, decimal> NoAttribution = new Dictionary<string, decimal>();

        private static TeamSeason Team(int rosterId, string name, params (decimal Actual, decimal Ideal, bool IsFinal)[] weeks) =>
            new(rosterId, name,
                [.. weeks.Select((w, i) => new TeamWeek(rosterId, i + 1, w.Actual, w.Ideal, [], [], [], w.IsFinal, NoAttribution, null))]);

        /// <summary>A team whose weeks carry real slot attribution, for T47/T48.</summary>
        private static TeamSeason TeamWithSlots(
            int rosterId, string name,
            params (bool IsFinal, IReadOnlyDictionary<string, decimal> Slots)[] weeks) =>
            new(rosterId, name,
                [.. weeks.Select((w, i) => new TeamWeek(rosterId, i + 1, 0m, 0m, [], [], [], w.IsFinal, w.Slots, null))]);

        /// <summary>Puts every week of a team in one game.</summary>
        private static TeamSeason InGame(TeamSeason team, int matchupId) =>
            team with { Weeks = [.. team.Weeks.Select(w => w with { MatchupId = matchupId })] };

        private static SeasonSnapshot Snapshot(
            IReadOnlyList<TeamSeason>? teams = null,
            string[]? rosterPositions = null,
            int currentWeek = 5,
            int finalThroughWeek = 4,
            int cutoff = RegularSeason.NFLWeeks,
            LeagueFormat format = LeagueFormat.TotalPoints) =>
            new("", 2026, currentWeek, finalThroughWeek,
                Slot.Parse(rosterPositions ?? ["QB", "QB", "RB", "RB", "WR", "WR", "TE", "FLEX", "SUPER_FLEX", "K", "DEF", "BN"]),
                teams ?? [], [], [], IsCurrent: true, cutoff, format, [], false, []);

        private static TeamSeason FourFinalWeeks(int rosterId = 1, string name = "Steady") =>
            Team(rosterId, name,
                (8m, 10m, true), (12m, 14m, true), (8m, 9m, true), (12m, 13m, true));

        [Fact]
        public void The_excluded_rosters_and_the_records_change_nothing_in_a_total_points_payload()
        {
            // T68: they are stored now, so prove they reach no metric, not even the
            // trimming of unplayed weeks: in week 3 only the placeholder scored. With
            // the placeholder every game pairs, so the records are known and must stay hidden
            var teams = new List<TeamSeason>
            {
                InGame(Team(1, "One", (10m, 11m, true), (20m, 21m, true), (0m, 0m, true)), 1),
                InGame(Team(2, "Two", (12m, 13m, true), (22m, 23m, true), (0m, 0m, true)), 1),
                InGame(Team(3, "Three", (14m, 15m, true), (24m, 25m, true), (0m, 0m, true)), 2),
            };
            var without = Snapshot(teams, currentWeek: 4, finalThroughWeek: 3);
            var stored = without with { ExcludedTeams = [InGame(Team(9, "Placeholder", (1m, 50m, true), (2m, 50m, true), (5m, 50m, true)), 2)] };
            stored.Records.Should().NotBeNull("the test needs a record that could leak");

            var payload = SeasonPayloadMapper.ToSeasonPayload(stored, AsOf);
            var body = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
            body.Should().Be(JsonSerializer.Serialize(SeasonPayloadMapper.ToSeasonPayload(without, AsOf), JsonSerializerOptions.Web));
            body.Should().NotContainEquivalentOf("record");
            JsonSerializer.Serialize(payload).Should().NotContainEquivalentOf("record");

            // the ETag of the total-points body; a record key would change it (A39)
            SeasonETag.Compute(payload).Should().Be("W/\"2026-wk4-07143DCD8688EFE0\"");
        }

        [Fact]
        public void Head_to_head_puts_each_counted_team_s_record_on_it_and_never_the_placeholder_s()
        {
            // A1: the placeholder's game counts for its opponent, but its own record is never shown
            var snapshot = Snapshot([
                InGame(Team(1, "One", (10m, 11m, true), (20m, 21m, true)), 1),
                InGame(Team(2, "Two", (12m, 13m, true), (8m, 23m, true)), 1),
                InGame(Team(3, "Three", (14m, 15m, true), (24m, 25m, true)), 2),
            ], currentWeek: 3, finalThroughWeek: 2, format: LeagueFormat.HeadToHead) with
            {
                ExcludedTeams = [InGame(Team(9, "Placeholder", (1m, 50m, true), (30m, 50m, true)), 2)],
            };

            var payload = SeasonPayloadMapper.ToSeasonPayload(snapshot, AsOf);

            payload.Teams.ToDictionary(t => t.RosterId, t => t.Record).Should().BeEquivalentTo(new Dictionary<int, RecordPayload>
            {
                [1] = new(1, 1, 0, 30m, 20m),
                [2] = new(1, 1, 0, 20m, 30m),
                [3] = new(1, 1, 0, 38m, 31m),
            });
            JsonSerializer.Serialize(payload, JsonSerializerOptions.Web).Should().NotContain("Placeholder");
        }

        [Fact]
        public void Head_to_head_before_any_week_settles_gives_every_team_no_wins_and_no_losses()
        {
            // the records are known but empty: nobody has a counted week yet
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                InGame(Team(1, "One", (10m, 11m, false)), 1),
                InGame(Team(2, "Two", (12m, 13m, false)), 1),
            ], currentWeek: 1, finalThroughWeek: 0, format: LeagueFormat.HeadToHead), AsOf);

            payload.Teams.Should().OnlyContain(t => t.Record == new RecordPayload(0, 0, 0, 0m, 0m));
        }

        [Fact]
        public void Head_to_head_leaves_the_record_out_while_a_counted_week_lacks_its_games()
        {
            // all or nothing: a week stored before matchups were read
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                InGame(Team(1, "One", (10m, 11m, true)), 1),
                Team(2, "Two", (12m, 13m, true)),
            ], currentWeek: 2, finalThroughWeek: 1, format: LeagueFormat.HeadToHead), AsOf);

            payload.Teams.Should().OnlyContain(t => t.Record == null);
            JsonSerializer.Serialize(payload, JsonSerializerOptions.Web).Should().NotContain("\"record\"");
        }

        [Fact]
        public void The_format_is_sent_by_name()
        {
            // A38: as the host serialises it, camelCase field and the setting's spelling
            var json = JsonSerializer.Serialize(
                SeasonPayloadMapper.ToSeasonPayload(Snapshot(format: LeagueFormat.HeadToHead), AsOf),
                JsonSerializerOptions.Web);

            json.Should().Contain("""
                "format":"HeadToHead"
                """);
        }

        [Fact]
        public void The_header_fields_come_straight_from_the_snapshot()
        {
            var payload = SeasonPayloadMapper.ToSeasonPayload(
                Snapshot(currentWeek: 7, finalThroughWeek: 6), AsOf);

            payload.Season.Should().Be(2026);
            payload.CurrentWeek.Should().Be(7);
            payload.FinalThroughWeek.Should().Be(6);
            payload.AsOfUtc.Should().Be(AsOf);
        }

        [Fact]
        public void The_served_season_is_the_current_one() => SeasonPayloadMapper.ToSeasonPayload(Snapshot(), AsOf).IsCurrent.Should().BeTrue();

        [Fact]
        public void Slot_order_is_the_distinct_codes_not_every_slot()
        {
            // eleven startable slots, eight radar axes
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot(), AsOf);

            payload.SlotOrder.Should().Equal(
                "QB", "RB", "WR", "TE", "FLEX", "SUPER FLEX", "K", "DEF");
        }

        [Fact]
        public void Slot_order_keeps_roster_positions_order_rather_than_sorting()
        {
            // R2: the silhouette depends on axis order, so it is never re-sorted
            var payload = SeasonPayloadMapper.ToSeasonPayload(
                Snapshot(rosterPositions: ["WR", "QB", "TE", "BN"]), AsOf);

            payload.SlotOrder.Should().Equal("WR", "QB", "TE");
        }

        [Fact]
        public void Bench_slots_never_become_axes()
        {
            var payload = SeasonPayloadMapper.ToSeasonPayload(
                Snapshot(rosterPositions: ["QB", "BN", "IR", "TAXI"]), AsOf);

            payload.SlotOrder.Should().Equal("QB");
        }

        [Fact]
        public void Teams_arrive_ranked_by_points()
        {
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                Team(3, "Third", (10m, 10m, true)),
                Team(1, "First", (30m, 30m, true)),
                Team(2, "Second", (20m, 20m, true)),
            ]), AsOf);

            payload.Teams.Select(t => t.Name).Should().Equal("First", "Second", "Third");
        }

        [Fact]
        public void Totals_sum_every_week_including_the_unfinished_one()
        {
            // the league is decided on total points, which includes the live week
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                Team(1, "Playing", (10m, 20m, true), (5m, 30m, false)),
            ]), AsOf);

            payload.Teams[0].Totals.Actual.Should().Be(15m);
            payload.Teams[0].Totals.Ideal.Should().Be(50m);
        }

        [Fact]
        public void Sigma_covers_finalized_weeks_only()
        {
            // R1: a team on 41 points at 2pm Sunday is not an outlier
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                Team(1, "Mid Sunday",
                    (8m, 8m, true), (12m, 12m, true), (8m, 8m, true), (12m, 12m, true),
                    (41m, 41m, false)),
            ]), AsOf);

            var totals = payload.Teams[0].Totals;
            totals.Actual.Should().Be(81m);
            totals.Sigma.Should().Be(2.0);
            totals.Max.Should().Be(12m, "41 belongs to a week that has not finished");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Playoff_weeks_count_toward_nothing_final_or_live(bool playoffWeekFinal)
        {
            // Q4: regular season only; week 5 is the first playoff week and a huge one
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                Team(1, "Playoff Hero",
                    (8m, 9m, true), (12m, 13m, true), (8m, 9m, true), (12m, 13m, true),
                    (200m, 210m, playoffWeekFinal)),
            ], currentWeek: playoffWeekFinal ? 6 : 5, finalThroughWeek: playoffWeekFinal ? 5 : 4, cutoff: 4), AsOf);

            var team = payload.Teams[0];
            team.Totals.Actual.Should().Be(40m);
            team.Totals.Ideal.Should().Be(44m);
            team.Totals.Sigma.Should().Be(2.0);
            team.Totals.Max.Should().Be(12m);
            team.Weeks.Select(w => w.Week).Should().Equal(1, 2, 3, 4);
            payload.FinalThroughWeek.Should().Be(4, "sigma's label must not promise a playoff week");
            payload.LastRegularWeek.Should().Be(4, "the page says the season is complete from it");
            payload.CurrentWeek.Should().BeGreaterThan(4, "the masthead still says what week it is");
        }

        [Fact]
        public void The_last_regular_week_still_in_play_is_counted_and_not_yet_final()
        {
            // week 2 is live and the last that counts; the season completes when it finalizes
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                Team(1, "Last Week", (10m, 11m, true), (20m, 21m, false)),
            ], currentWeek: 2, finalThroughWeek: 1, cutoff: 2), AsOf);

            payload.Teams[0].Totals.Actual.Should().Be(30m);
            payload.FinalThroughWeek.Should().Be(1);
        }

        [Fact]
        public void A_season_that_ends_before_week_18_ends_at_its_last_played_week()
        {
            // the cut-off (18 in life, 3 here) has settled with nobody scoring: the league ended at week 2
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                Team(1, "Short", (10m, 11m, true), (20m, 21m, true), (0m, 0m, true)),
                Team(2, "Season", (12m, 13m, true), (22m, 23m, true), (0m, 0m, true)),
            ], currentWeek: 4, finalThroughWeek: 3, cutoff: 3), AsOf);

            payload.LastRegularWeek.Should().Be(2);
            payload.FinalThroughWeek.Should().Be(2, "the season is complete, so the page says so");
            payload.Teams.Should().OnlyContain(t => t.Weeks.Select(w => w.Week).SequenceEqual(new[] { 1, 2 }));
        }

        [Fact]
        public void Playoff_weeks_stay_off_the_radar()
        {
            var regular = new Dictionary<string, decimal> { ["QB"] = 10m };
            var playoff = new Dictionary<string, decimal> { ["QB"] = 90m };
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                TeamWithSlots(1, "Radar", (true, regular), (true, playoff)),
            ], cutoff: 1), AsOf);

            payload.Teams[0].SlotAverages["QB"].Should().Be(10m);
            payload.LeagueSlotAverages["QB"].Should().Be(10m);
        }

        [Fact]
        public void Sigma_and_its_companions_are_null_below_four_final_weeks()
        {
            // D1: absence is a value the client has to handle, not a zero
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                Team(1, "Too Early", (10m, 10m, true), (20m, 20m, true), (30m, 30m, true)),
            ]), AsOf);

            var totals = payload.Teams[0].Totals;
            totals.Sigma.Should().BeNull();
            totals.Cv.Should().BeNull();
            totals.Mean.Should().BeNull();
            totals.Min.Should().BeNull();
            totals.Max.Should().BeNull();
            totals.Actual.Should().Be(60m, "the points column still works");
        }

        [Fact]
        public void Totals_carry_the_figures_the_range_mark_needs()
        {
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([FourFinalWeeks()]), AsOf);
            var totals = payload.Teams[0].Totals;

            totals.Mean.Should().Be(10m);
            totals.Min.Should().Be(8m);
            totals.Max.Should().Be(12m);
            totals.Cv.Should().BeApproximately(0.2, 1e-12);
        }

        [Fact]
        public void Weeks_come_through_in_order_with_their_finality()
        {
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                Team(1, "One", (10m, 11m, true), (20m, 21m, true), (30m, 31m, false)),
            ]), AsOf);

            var weeks = payload.Teams[0].Weeks;
            weeks.Select(w => w.Week).Should().Equal(1, 2, 3);
            weeks.Select(w => w.IsFinal).Should().Equal(true, true, false);
            weeks[2].Actual.Should().Be(30m);
            weeks[2].Ideal.Should().Be(31m);
        }

        [Fact]
        public void The_radar_blocks_are_empty_when_no_week_produced_an_attribution()
        {
            // an empty object beats omitting the key; the client can bind either way
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([FourFinalWeeks()]), AsOf);

            payload.LeagueSlotAverages.Should().NotBeNull().And.BeEmpty();
            payload.Teams[0].SlotAverages.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void Slot_averages_flow_from_weeks_to_the_team_and_the_league_reference()
        {
            // T47: average each team's weeks, then average across teams
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                TeamWithSlots(1, "High",
                    (true, new Dictionary<string, decimal> { ["QB"] = 20m }),
                    (true, new Dictionary<string, decimal> { ["QB"] = 24m })),
                TeamWithSlots(2, "Low",
                    (true, new Dictionary<string, decimal> { ["QB"] = 10m }),
                    (true, new Dictionary<string, decimal> { ["QB"] = 10m })),
            ]), AsOf);

            payload.Teams.Single(t => t.RosterId == 1).SlotAverages["QB"].Should().Be(22m);
            payload.Teams.Single(t => t.RosterId == 2).SlotAverages["QB"].Should().Be(10m);
            payload.LeagueSlotAverages["QB"].Should().Be(16m, "the two teams' season averages, averaged");
        }

        [Fact]
        public void An_unfinished_week_and_a_rules_mismatch_week_do_not_count_toward_the_average()
        {
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                TeamWithSlots(1, "Mixed",
                    (true, new Dictionary<string, decimal> { ["QB"] = 20m }),
                    (false, new Dictionary<string, decimal> { ["QB"] = 99m }),           // not final
                    (true, NoAttribution)),                                              // rules guard excluded it
            ]), AsOf);

            payload.Teams[0].SlotAverages["QB"].Should().Be(20m);
        }

        [Fact]
        public void A_season_with_no_teams_maps_without_throwing()
        {
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot(), AsOf);

            payload.Teams.Should().BeEmpty();
            payload.SlotOrder.Should().NotBeEmpty();
        }

        [Fact]
        public void The_payload_serialises_to_the_documented_field_names()
        {
            // ASP.NET Core's defaults are camelCase; the contract in §09 is camelCase
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([FourFinalWeeks()]), AsOf);

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

            foreach (var field in new[]
            {
                "season", "isCurrent", "currentWeek", "finalThroughWeek", "asOfUtc",
                "slotOrder", "leagueSlotAverages", "teams",
                "rosterId", "name", "totals", "slotAverages", "weeks",
                "actual", "ideal", "sigma", "cv", "mean", "min", "max", "isFinal",
            })
            {
                json.Should().Contain($"\"{field}\"", $"§09 names the field {field}");
            }
        }

        [Fact]
        public void A_null_sigma_serialises_as_null_rather_than_disappearing()
        {
            var payload = SeasonPayloadMapper.ToSeasonPayload(Snapshot([
                Team(1, "Too Early", (10m, 10m, true)),
            ]), AsOf);

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

            json.Should().Contain("\"sigma\":null");
        }
    }
}
