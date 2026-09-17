using System.Text.Json;
using FantasyLeague.Sleeper;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    public class SeasonPayloadMapperTests
    {
        private static readonly DateTimeOffset AsOf = new(2026, 10, 18, 18, 42, 3, TimeSpan.Zero);

        private static readonly IReadOnlyDictionary<string, decimal> NoAttribution = new Dictionary<string, decimal>();

        private static TeamSeason Team(int rosterId, string name, params (decimal Actual, decimal Ideal, bool IsFinal)[] weeks) =>
            new(rosterId, name,
                [.. weeks.Select((w, i) => new TeamWeek(rosterId, i + 1, w.Actual, w.Ideal, [], [], [], w.IsFinal, NoAttribution))]);

        /// <summary>A team whose weeks carry real slot attribution, for T47/T48.</summary>
        private static TeamSeason TeamWithSlots(
            int rosterId, string name,
            params (bool IsFinal, IReadOnlyDictionary<string, decimal> Slots)[] weeks) =>
            new(rosterId, name,
                [.. weeks.Select((w, i) => new TeamWeek(rosterId, i + 1, 0m, 0m, [], [], [], w.IsFinal, w.Slots))]);

        private static SeasonSnapshot Snapshot(
            IReadOnlyList<TeamSeason>? teams = null,
            string[]? rosterPositions = null,
            int currentWeek = 5,
            int finalThroughWeek = 4,
            IReadOnlyDictionary<int, string>? owners = null) =>
            new("", 2026, currentWeek, finalThroughWeek,
                Slot.Parse(rosterPositions ?? ["QB", "QB", "RB", "RB", "WR", "WR", "TE", "FLEX", "SUPER_FLEX", "K", "DEF", "BN"]),
                teams ?? [], [], [], owners ?? new Dictionary<int, string>());

        private static TeamSeason FourFinalWeeks(int rosterId = 1, string name = "Steady") =>
            Team(rosterId, name,
                (8m, 10m, true), (12m, 14m, true), (8m, 9m, true), (12m, 13m, true));

        [Fact]
        public void The_header_fields_come_straight_from_the_snapshot()
        {
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(
                Snapshot(currentWeek: 7, finalThroughWeek: 6), AsOf);

            payload.Season.Should().Be(2026);
            payload.CurrentWeek.Should().Be(7);
            payload.FinalThroughWeek.Should().Be(6);
            payload.AsOfUtc.Should().Be(AsOf);
        }

        [Fact]
        public void No_requested_season_means_the_current_one()
        {
            SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot(), AsOf).IsCurrent.Should().BeTrue();
        }

        [Fact]
        public void Requesting_the_season_being_served_is_still_current()
        {
            SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot(), AsOf, requestedSeason: 2026)
                .IsCurrent.Should().BeTrue();
        }

        [Fact]
        public void Requesting_a_different_season_is_not_current()
        {
            // decides the cache headers and whether the page keeps polling
            SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot(), AsOf, requestedSeason: 2025)
                .IsCurrent.Should().BeFalse();
        }

        [Fact]
        public void Slot_order_is_the_distinct_codes_not_every_slot()
        {
            // eleven startable slots, eight radar axes
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot(), AsOf);

            payload.SlotOrder.Should().Equal(
                "QB", "RB", "WR", "TE", "FLEX", "SUPER FLEX", "K", "DEF");
        }

        [Fact]
        public void Slot_order_keeps_roster_positions_order_rather_than_sorting()
        {
            // R2: the silhouette depends on axis order, so it is never re-sorted
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(
                Snapshot(rosterPositions: ["WR", "QB", "TE", "BN"]), AsOf);

            payload.SlotOrder.Should().Equal("WR", "QB", "TE");
        }

        [Fact]
        public void Bench_slots_never_become_axes()
        {
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(
                Snapshot(rosterPositions: ["QB", "BN", "IR", "TAXI"]), AsOf);

            payload.SlotOrder.Should().Equal("QB");
        }

        [Fact]
        public void Teams_arrive_ranked_by_points()
        {
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([
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
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([
                Team(1, "Playing", (10m, 20m, true), (5m, 30m, false)),
            ]), AsOf);

            payload.Teams[0].Totals.Actual.Should().Be(15m);
            payload.Teams[0].Totals.Ideal.Should().Be(50m);
        }

        [Fact]
        public void Sigma_covers_finalized_weeks_only()
        {
            // R1: a team on 41 points at 2pm Sunday is not an outlier
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([
                Team(1, "Mid Sunday",
                    (8m, 8m, true), (12m, 12m, true), (8m, 8m, true), (12m, 12m, true),
                    (41m, 41m, false)),
            ]), AsOf);

            var totals = payload.Teams[0].Totals;
            totals.Actual.Should().Be(81m);
            totals.Sigma.Should().Be(2.0);
            totals.Max.Should().Be(12m, "41 belongs to a week that has not finished");
        }

        [Fact]
        public void Sigma_and_its_companions_are_null_below_four_final_weeks()
        {
            // D1: absence is a value the client has to handle, not a zero
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([
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
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([FourFinalWeeks()]), AsOf);
            var totals = payload.Teams[0].Totals;

            totals.Mean.Should().Be(10m);
            totals.Min.Should().Be(8m);
            totals.Max.Should().Be(12m);
            totals.Cv.Should().BeApproximately(0.2, 1e-12);
        }

        [Fact]
        public void Weeks_come_through_in_order_with_their_finality()
        {
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([
                Team(1, "One", (10m, 11m, true), (20m, 21m, true), (30m, 31m, false)),
            ]), AsOf);

            var weeks = payload.Teams[0].Weeks;
            weeks.Select(w => w.Week).Should().Equal(1, 2, 3);
            weeks.Select(w => w.IsFinal).Should().Equal(true, true, false);
            weeks[2].Actual.Should().Be(30m);
            weeks[2].Ideal.Should().Be(31m);
        }

        [Fact]
        public void An_excluded_roster_is_dropped_entirely()
        {
            // A1, at the read boundary
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(
                Snapshot([
                    Team(1, "Real", (30m, 30m, true)),
                    Team(9, "POON SQUAD", (0m, 0m, true)),
                ]),
                AsOf,
                excludedRosterIds: new HashSet<int> { 9 });

            payload.Teams.Should().ContainSingle().Which.RosterId.Should().Be(1);
        }

        [Fact]
        public void Excluding_nobody_keeps_everybody()
        {
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([
                Team(1, "One", (10m, 10m, true)),
                Team(2, "Two", (20m, 20m, true)),
            ]), AsOf);

            payload.Teams.Should().HaveCount(2);
        }

        [Fact]
        public void The_radar_blocks_are_empty_when_no_week_produced_an_attribution()
        {
            // an empty object beats omitting the key; the client can bind either way
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([FourFinalWeeks()]), AsOf);

            payload.LeagueSlotAverages.Should().NotBeNull().And.BeEmpty();
            payload.Teams[0].SlotAverages.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void Slot_averages_flow_from_weeks_to_the_team_and_the_league_reference()
        {
            // T47: average each team's weeks, then average across teams
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([
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
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([
                TeamWithSlots(1, "Mixed",
                    (true, new Dictionary<string, decimal> { ["QB"] = 20m }),
                    (false, new Dictionary<string, decimal> { ["QB"] = 99m }),           // not final
                    (true, NoAttribution)),                                              // rules guard excluded it
            ]), AsOf);

            payload.Teams[0].SlotAverages["QB"].Should().Be(20m);
        }

        [Fact]
        public void The_league_reference_excludes_the_dummy_team_A1_step_2()
        {
            // A1: a second, separate filter from the standings exclusion above
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(
                Snapshot([
                    TeamWithSlots(1, "Real", (true, new Dictionary<string, decimal> { ["QB"] = 20m })),
                    TeamWithSlots(9, "POON SQUAD", (true, new Dictionary<string, decimal> { ["QB"] = 0m })),
                ]),
                AsOf,
                excludedRosterIds: new HashSet<int> { 9 });

            payload.LeagueSlotAverages["QB"].Should().Be(20m, "the dummy team's zero must not drag the reference down");
        }

        [Fact]
        public void A_season_with_no_teams_maps_without_throwing()
        {
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot(), AsOf);

            payload.Teams.Should().BeEmpty();
            payload.SlotOrder.Should().NotBeEmpty();
        }

        [Fact]
        public void The_payload_serialises_to_the_documented_field_names()
        {
            // ASP.NET Core's defaults are camelCase; the contract in §09 is camelCase
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([FourFinalWeeks()]), AsOf);

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
            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(Snapshot([
                Team(1, "Too Early", (10m, 10m, true)),
            ]), AsOf);

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

            json.Should().Contain("\"sigma\":null");
        }
    }

    public class SeasonsPayloadMapperTests
    {
        [Fact]
        public void Only_the_current_season_is_available_before_the_chain_walk()
        {
            var payload = SeasonPayloadMapper.ToSeasonsPayload(2026);

            payload.Current.Should().Be(2026);
            payload.Available.Should().Equal(2026);
        }

        [Fact]
        public void Available_seasons_come_back_newest_first()
        {
            // what the season picker binds to, so a new year appears the day D8 finds it
            var payload = SeasonPayloadMapper.ToSeasonsPayload(2026, [2024, 2026, 2025]);

            payload.Available.Should().Equal(2026, 2025, 2024);
        }

        [Fact]
        public void A_repeated_season_is_listed_once()
        {
            SeasonPayloadMapper.ToSeasonsPayload(2026, [2026, 2026, 2025])
                .Available.Should().Equal(2026, 2025);
        }
    }
}
