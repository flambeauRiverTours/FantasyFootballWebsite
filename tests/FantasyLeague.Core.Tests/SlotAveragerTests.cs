using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Covers <see cref="SlotAverager"/>: T46's per-week attribution and T47's
    /// season and league averages.
    /// </summary>
    public class WeekAveragesTests
    {
        private static readonly IReadOnlyList<Slot> TwoRbFlex = Slot.Parse(["QB", "RB", "RB", "FLEX"]);
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoPositions =
            new Dictionary<string, IReadOnlyList<string>>();

        private static IReadOnlyDictionary<string, decimal>? Averages(
            string?[] starters,
            Dictionary<string, decimal> points,
            IReadOnlyList<Slot> slots,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? positions = null) =>
            SlotAverager.WeekAverages(starters, points, positions ?? NoPositions, slots);

        [Fact]
        public void A_two_RB_league_produces_an_RB_average_not_a_total()
        {
            var starters = new[] { "qb1", "rb1", "rb2", "wr1" };
            var points = new Dictionary<string, decimal> { ["qb1"] = 20m, ["rb1"] = 10m, ["rb2"] = 6m, ["wr1"] = 8m };

            var averages = Averages(starters, points, TwoRbFlex);

            averages!["RB"].Should().Be(8m, "two RBs divided by two slots, not their total");
        }

        [Fact]
        public void An_empty_slot_contributes_a_real_zero_and_still_counts()
        {
            var starters = new[] { "qb1", "rb1", null, "wr1" };
            var points = new Dictionary<string, decimal> { ["qb1"] = 20m, ["rb1"] = 10m, ["wr1"] = 8m };

            var averages = Averages(starters, points, TwoRbFlex);

            averages!["RB"].Should().Be(5m, "an empty slot drags the average down, not out of the divisor");
        }

        [Fact]
        public void A_starter_missing_from_players_points_scores_zero_rather_than_throwing()
        {
            var starters = new[] { "qb1", "rb1", "rb2", "wr1" };
            var points = new Dictionary<string, decimal> { ["qb1"] = 20m, ["rb1"] = 10m };

            var act = () => Averages(starters, points, TwoRbFlex);

            act.Should().NotThrow();
        }

        [Fact]
        public void A_defence_starter_resolves_like_any_other_id()
        {
            var starters = new[] { "DEN" };
            var points = new Dictionary<string, decimal> { ["DEN"] = 9m };

            Averages(starters, points, Slot.Parse(["DEF"]))!["DEF"].Should().Be(9m);
        }

        [Fact]
        public void A_starters_count_mismatch_returns_null_the_mid_season_rules_guard()
        {
            var starters = new[] { "qb1", "rb1" };

            Averages(starters, [], TwoRbFlex).Should().BeNull();
        }

        [Fact]
        public void A_same_count_slot_reorder_returns_null_when_a_starter_is_ineligible()
        {
            // RB,RB,FLEX -> RB,WR,FLEX: same cardinality, so the count guard alone
            // does not catch it. An RB-only player started under today's WR slot
            // is the tell that this week predates the reorder.
            var reordered = Slot.Parse(["QB", "RB", "WR", "FLEX"]);
            var starters = new[] { "qb1", "rb1", "rb2", "wr1" };
            var points = new Dictionary<string, decimal> { ["qb1"] = 20m, ["rb1"] = 10m, ["rb2"] = 6m, ["wr1"] = 8m };
            var positions = new Dictionary<string, IReadOnlyList<string>>
            {
                ["qb1"] = ["QB"],
                ["rb1"] = ["RB"],
                ["rb2"] = ["RB"],           // started in the WR slot, but only RB-eligible
                ["wr1"] = ["WR"],
            };

            Averages(starters, points, reordered, positions).Should().BeNull();
        }

        [Fact]
        public void An_unresolved_starter_is_not_treated_as_a_slot_mismatch()
        {
            // no position data for an unresolved id - a separate, already-logged
            // trap, not evidence of a reorder
            var starters = new[] { "unknown" };
            var points = new Dictionary<string, decimal>();

            Averages(starters, points, Slot.Parse(["RB"])).Should().NotBeNull();
        }

        [Fact]
        public void A_flex_slot_accepts_any_eligible_position()
        {
            var starters = new[] { "wr1" };
            var points = new Dictionary<string, decimal> { ["wr1"] = 12m };
            var positions = new Dictionary<string, IReadOnlyList<string>> { ["wr1"] = ["WR"] };

            Averages(starters, points, Slot.Parse(["FLEX"]), positions)!["FLEX"].Should().Be(12m);
        }
    }

    public class SeasonAveragesTests
    {
        private static TeamWeek Week(bool isFinal, IReadOnlyDictionary<string, decimal> slots) =>
            new(1, 1, 0m, 0m, [], [], [], isFinal, slots);

        [Fact]
        public void Finalized_weeks_with_an_attribution_are_averaged()
        {
            var weeks = new[]
            {
                Week(true, new Dictionary<string, decimal> { ["QB"] = 20m }),
                Week(true, new Dictionary<string, decimal> { ["QB"] = 24m }),
            };

            SlotAverager.SeasonAverages(weeks)["QB"].Should().Be(22m);
        }

        [Fact]
        public void An_unfinished_week_is_excluded()
        {
            var weeks = new[]
            {
                Week(true, new Dictionary<string, decimal> { ["QB"] = 20m }),
                Week(false, new Dictionary<string, decimal> { ["QB"] = 99m }),
            };

            SlotAverager.SeasonAverages(weeks)["QB"].Should().Be(20m);
        }

        [Fact]
        public void A_week_the_rules_guard_excluded_is_skipped_not_zero()
        {
            var weeks = new[]
            {
                Week(true, new Dictionary<string, decimal> { ["QB"] = 20m }),
                Week(true, new Dictionary<string, decimal>()),
            };

            SlotAverager.SeasonAverages(weeks)["QB"].Should().Be(20m, "an empty attribution must not count as a zero week");
        }

        [Fact]
        public void No_attributed_weeks_returns_empty_not_a_divide_by_zero()
        {
            var weeks = new[] { Week(false, new Dictionary<string, decimal> { ["QB"] = 20m }) };

            SlotAverager.SeasonAverages(weeks).Should().BeEmpty();
        }
    }

    public class LeagueAveragesTests
    {
        [Fact]
        public void The_reference_is_the_mean_of_each_teams_season_average()
        {
            var teams = new[]
            {
                new Dictionary<string, decimal> { ["QB"] = 20m },
                new Dictionary<string, decimal> { ["QB"] = 10m },
            };

            SlotAverager.LeagueAverages(teams)["QB"].Should().Be(15m);
        }

        [Fact]
        public void A_slot_only_some_teams_reached_averages_over_those_teams_only()
        {
            var teams = new[]
            {
                new Dictionary<string, decimal> { ["QB"] = 20m, ["SUPER FLEX"] = 8m },
                new Dictionary<string, decimal> { ["QB"] = 10m },
            };

            var league = SlotAverager.LeagueAverages(teams);

            league["QB"].Should().Be(15m);
            league["SUPER FLEX"].Should().Be(8m, "only one team ever started the slot; that is still the reference");
        }

        [Fact]
        public void No_teams_returns_empty_not_a_divide_by_zero() => SlotAverager.LeagueAverages([]).Should().BeEmpty();
    }
}
