using System.Text.Json;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Covers <see cref="Slot.Parse"/>: which codes survive, and in what order.
    /// </summary>
    public class SlotParseTests
    {
        [Theory]
        [InlineData("BN")]
        [InlineData("IR")]
        [InlineData("TAXI")]
        public void Parse_drops_non_startable_codes(string code)
        {
            Slot.Parse(["QB", code, "RB"])
                .Select(s => s.Code)
                .Should().Equal("QB", "RB");
        }

        [Fact]
        public void Parse_returns_one_slot_per_entry_including_duplicates()
        {
            // roster_positions repeats a code once per slot: two RB entries are
            // two RB slots, so the list needs no expansion.
            Slot.Parse(["RB", "RB", "WR", "WR", "WR"])
                .Select(s => s.Code)
                .Should().Equal("RB", "RB", "WR", "WR", "WR");
        }

        [Fact]
        public void Parse_preserves_roster_positions_order()
        {
            // Load-bearing, and easy to break by tidying Parse with a Distinct or
            // an OrderBy. A5 established that starters[i] maps to slots[i], so
            // reordering here silently misattributes every slot on the radar.
            string[] rosterPositions =
                ["QB", "QB", "RB", "RB", "WR", "WR", "TE", "FLEX", "SUPER FLEX", "K", "DEF"];

            Slot.Parse(rosterPositions)
                .Select(s => s.Code)
                .Should().Equal(rosterPositions);
        }

        [Fact]
        public void Parse_returns_empty_when_nothing_is_startable()
        {
            Slot.Parse(["BN", "BN", "IR"]).Should().BeEmpty();
        }

        [Fact(Skip = "Enable when A3's unknown-slot guard lands; see docs/amendments.md.")]
        public void Parse_rejects_an_unrecognised_slot_code()
        {
            // Today an unknown code becomes a startable slot that accepts nobody,
            // so ideal is silently undercounted -- and ideal >= actual cannot
            // catch it, because undercounting is the restrictive direction.
            var act = () => Slot.Parse(["QB", "WR/TE FLEX"]);

            act.Should().Throw<ArgumentException>()
               .WithMessage("*WR/TE FLEX*");
        }
    }

    /// <summary>
    /// Covers <see cref="Slot.Accepts"/>: which players may fill which slot.
    /// </summary>
    public class SlotEligibilityTests
    {
        [Theory]
        [InlineData("QB")]
        [InlineData("RB")]
        [InlineData("WR")]
        [InlineData("TE")]
        [InlineData("K")]
        [InlineData("DEF")]
        public void A_position_slot_accepts_only_its_own_position(string position)
        {
            var slot = new Slot(position);
            var others = new[] { "QB", "RB", "WR", "TE", "K", "DEF" }
                .Where(p => p != position)
                .ToList();

            slot.Accepts([position]).Should().BeTrue();
            slot.Accepts(others).Should().BeFalse();
        }

        [Theory]
        [InlineData("FLEX", "RB", "WR", "TE")]
        [InlineData("SUPER FLEX", "QB", "RB", "WR", "TE")]
        [InlineData("REC FLEX", "WR", "TE")]
        [InlineData("WR/RB FLEX", "RB", "WR")]
        [InlineData("IDP FLEX", "DL", "LB", "DB")]
        public void A_flex_slot_accepts_every_position_it_allows(
            string code, params string[] allowed)
        {
            var slot = new Slot(code);

            foreach (var position in allowed)
            {
                slot.Accepts([position]).Should()
                    .BeTrue($"{code} should accept {position}");
            }
        }

        [Theory]
        [InlineData("FLEX", "QB", "K", "DEF")]
        [InlineData("SUPER FLEX", "K", "DEF")]
        [InlineData("REC FLEX", "QB", "RB", "K")]
        [InlineData("WR/RB FLEX", "QB", "TE", "K")]
        [InlineData("IDP FLEX", "QB", "RB", "WR")]
        public void A_flex_slot_rejects_every_position_it_does_not_allow(
            string code, params string[] disallowed)
        {
            var slot = new Slot(code);

            foreach (var position in disallowed)
            {
                slot.Accepts([position]).Should()
                    .BeFalse($"{code} should reject {position}");
            }
        }

        [Fact]
        public void A_multi_eligible_player_fills_a_flex_he_qualifies_for()
        {
            // The T04 warning: match on fantasy_positions, not position. Sleeper
            // may list this player at position "RB" while fantasy_positions
            // carries ["RB","WR"] -- and the second entry is what makes him legal
            // in a WRRB_FLEX. Using the singular field silently undercounts ideal
            // for exactly the players who make flex slots interesting.
            string[] fantasyPositions = ["RB", "WR"];

            new Slot("WR/RB FLEX").Accepts(fantasyPositions).Should().BeTrue();
            new Slot("REC FLEX").Accepts(fantasyPositions).Should().BeTrue();
            new Slot("WR").Accepts(fantasyPositions).Should().BeTrue();
            new Slot("TE").Accepts(fantasyPositions).Should().BeFalse();
        }

        [Fact]
        public void Super_flex_accepts_a_quarterback()
        {
            // This league's live case: two QB slots and a SUPER_FLEX (Q2).
            new Slot("SUPER FLEX").Accepts(["QB"]).Should().BeTrue();
        }

        [Fact]
        public void A_slot_accepts_nobody_when_the_player_has_no_positions()
        {
            new Slot("FLEX").Accepts([]).Should().BeFalse();
            new Slot("QB").Accepts([]).Should().BeFalse();
        }
    }

    /// <summary>
    /// Parses the real roster_positions captured from the league, so the
    /// eligibility map is exercised against this league's actual slot list
    /// rather than an invented one.
    /// </summary>
    public class SlotFixtureTests
    {
        private static string[] RealRosterPositions()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "roster-positions.json");
            File.Exists(path).Should().BeTrue($"fixture missing at {path}");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement
                      .GetProperty("roster_positions")
                      .EnumerateArray()
                      .Select(e => e.GetString()!)
                      .ToArray();
        }

        [Fact]
        public void The_real_league_parses_to_its_eleven_startable_slots_in_order()
        {
            Slot.Parse(RealRosterPositions())
                .Select(s => s.Code)
                .Should().Equal(
                    "QB", "QB", "RB", "RB", "WR", "WR", "TE", "FLEX", "SUPER FLEX", "K", "DEF");
        }

        [Fact]
        public void The_real_league_has_eight_distinct_slots_for_the_radar_axes()
        {
            // Q8: eight axes is comfortably readable. Slot is a record, so
            // equality is by Code and Distinct gives the axis count for free.
            Slot.Parse(RealRosterPositions())
                .Distinct()
                .Should().HaveCount(8);
        }

        [Fact]
        public void Every_bench_slot_is_dropped_from_the_real_league()
        {
            var all = RealRosterPositions();
            var startable = Slot.Parse(all);

            all.Should().HaveCount(19);
            startable.Should().HaveCount(11);
            startable.Should().NotContain(
                s => s.Code == "BN" || s.Code == "IR" || s.Code == "TAXI");
        }
    }
}
