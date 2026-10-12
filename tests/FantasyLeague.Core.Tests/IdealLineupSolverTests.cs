using System.Text.Json;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    internal static class Roster
    {
        public static RosteredPlayer Player(string id, decimal points, params string[] positions) =>
            new(new PlayerId(id), id.ToUpperInvariant(), positions, points);

        public static IReadOnlyList<Slot> Slots(params string[] codes) => Slot.Parse(codes);
    }

    /// <summary>
    /// The points the solver returns: is it the true maximum?
    /// </summary>
    public class IdealLineupSolverPointsTests
    {
        [Fact]
        public void Flex_ordering_does_not_strand_a_player()
        {
            // The case that justifies the DP existing at all. A greedy pass that
            // sorts by points and takes the first eligible open slot can put the
            // WR into FLEX, leaving the RB with nowhere to go and a total of 20.
            var slots = Roster.Slots("WR", "FLEX");
            var players = new[]
            {
                Roster.Player("wr1", 20m, "WR"),
                Roster.Player("rb1", 18m, "RB"),
            };

            IdealLineupSolver.Solve(players, slots).IdealPts.Should().Be(38m);
        }

        [Fact]
        public void Slot_order_does_not_change_the_answer()
        {
            // Same two players, slots declared the other way round. An optimal
            // solver is indifferent; a greedy one is not.
            var players = new[]
            {
                Roster.Player("wr1", 20m, "WR"),
                Roster.Player("rb1", 18m, "RB"),
            };

            IdealLineupSolver.Solve(players, Roster.Slots("FLEX", "WR")).IdealPts.Should().Be(38m);
            IdealLineupSolver.Solve(players, Roster.Slots("WR", "FLEX")).IdealPts.Should().Be(38m);
        }

        [Fact]
        public void Player_order_does_not_change_the_answer()
        {
            var slots = Roster.Slots("WR", "FLEX");
            var a = Roster.Player("wr1", 20m, "WR");
            var b = Roster.Player("rb1", 18m, "RB");

            IdealLineupSolver.Solve([a, b], slots).IdealPts
                .Should().Be(IdealLineupSolver.Solve([b, a], slots).IdealPts);
        }

        [Fact]
        public void A_bye_week_hole_returns_the_best_partial_fill()
        {
            // Fewer eligible bodies than slots. The full mask is unreachable, so
            // taking dp.Max() rather than dp[full] is what keeps this from
            // returning -1 or throwing.
            var slots = Roster.Slots("QB", "RB", "RB", "WR");
            var players = new[]
            {
                Roster.Player("qb1", 22m, "QB"),
                Roster.Player("rb1", 11m, "RB"),
            };

            var (points, lineup) = IdealLineupSolver.Solve(players, slots);

            points.Should().Be(33m);
            lineup.Should().HaveCount(2);
        }

        [Fact]
        public void A_multi_eligible_player_is_placed_where_he_is_worth_most()
        {
            // fantasy_positions ["RB","WR"] makes this player legal in WRRB_FLEX.
            // Matching on the singular `position` field instead would undercount.
            var slots = Roster.Slots("RB", "WRRB_FLEX");
            var players = new[]
            {
                Roster.Player("rb1", 14m, "RB"),
                Roster.Player("hybrid", 12m, "RB", "WR"),
            };

            IdealLineupSolver.Solve(players, slots).IdealPts.Should().Be(26m);
        }

        [Fact]
        public void Super_flex_lets_a_third_quarterback_start()
        {
            // This league's shape: two QB slots plus a SUPER_FLEX (Q2).
            var slots = Roster.Slots("QB", "QB", "SUPER_FLEX");
            var players = new[]
            {
                Roster.Player("qb1", 25m, "QB"),
                Roster.Player("qb2", 22m, "QB"),
                Roster.Player("qb3", 20m, "QB"),
                Roster.Player("wr1", 18m, "WR"),
            };

            IdealLineupSolver.Solve(players, slots).IdealPts.Should().Be(67m);
        }

        [Fact]
        public void An_ineligible_player_is_never_started()
        {
            var slots = Roster.Slots("QB", "FLEX");
            var players = new[]
            {
                Roster.Player("qb1", 10m, "QB"),
                Roster.Player("k1", 99m, "K"),   // no slot accepts a kicker here
            };

            var (points, lineup) = IdealLineupSolver.Solve(players, slots);

            points.Should().Be(10m);
            lineup.Should().ContainSingle().Which.Value.Should().Be("qb1");
        }

        [Fact]
        public void No_players_scores_nothing()
        {
            var (points, lineup) = IdealLineupSolver.Solve([], Roster.Slots("QB", "FLEX"));

            points.Should().Be(0m);
            lineup.Should().BeEmpty();
        }

        [Fact]
        public void No_slots_scores_nothing()
        {
            var players = new[] { Roster.Player("qb1", 30m, "QB") };

            var (points, lineup) = IdealLineupSolver.Solve(players, Roster.Slots("BN", "BN"));

            points.Should().Be(0m);
            lineup.Should().BeEmpty();
        }

        [Fact]
        public void A_zero_scoring_player_still_fills_a_slot_without_changing_the_total()
        {
            var slots = Roster.Slots("QB", "RB");
            var players = new[]
            {
                Roster.Player("qb1", 18m, "QB"),
                Roster.Player("rb1", 0m, "RB"),
            };

            IdealLineupSolver.Solve(players, slots).IdealPts.Should().Be(18m);
        }
    }

    /// <summary>
    /// The lineup the solver returns: does it actually correspond to the points?
    /// </summary>
    public class IdealLineupSolverLineupTests
    {
        [Fact]
        public void The_lineup_names_every_player_counted_and_no_one_twice()
        {
            // Two receivers, two slots that both accept one. Both should start,
            // and the lineup should name both -- not the better one twice.
            var slots = Roster.Slots("WR", "FLEX");
            var players = new[]
            {
                Roster.Player("wr_low", 4m, "WR"),
                Roster.Player("wr_high", 17m, "WR"),
            };

            var (points, lineup) = IdealLineupSolver.Solve(players, slots);

            points.Should().Be(21m);
            lineup.Select(id => id.Value).Should().BeEquivalentTo(["wr_low", "wr_high"]);
        }

        [Fact]
        public void The_lineup_never_repeats_a_player()
        {
            var slots = Roster.Slots("WR", "FLEX");
            var players = new[]
            {
                Roster.Player("wr_low", 4m, "WR"),
                Roster.Player("wr_high", 17m, "WR"),
            };

            var lineup = IdealLineupSolver.Solve(players, slots).Lineup;

            lineup.Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void The_lineup_points_sum_to_the_points_returned()
        {
            var slots = Roster.Slots("QB", "RB", "FLEX");
            var players = new[]
            {
                Roster.Player("qb1", 21.5m, "QB"),
                Roster.Player("rb1", 9.2m, "RB"),
                Roster.Player("rb2", 14.8m, "RB"),
                Roster.Player("wr1", 6.4m, "WR"),
            };

            var (points, lineup) = IdealLineupSolver.Solve(players, slots);
            var byId = players.ToDictionary(p => p.Id);

            lineup.Sum(id => byId[id].Points).Should().Be(points);
        }

        [Fact]
        public void The_lineup_never_holds_more_players_than_there_are_slots()
        {
            var slots = Roster.Slots("QB", "FLEX");
            var players = new[]
            {
                Roster.Player("qb1", 20m, "QB"),
                Roster.Player("rb1", 15m, "RB"),
                Roster.Player("wr1", 12m, "WR"),
                Roster.Player("te1", 8m, "TE"),
            };

            IdealLineupSolver.Solve(players, slots).Lineup.Count
                .Should().BeLessThanOrEqualTo(slots.Count);
        }
    }

    /// <summary>
    /// Randomised, but seeded and therefore deterministic. Checks the solver
    /// against an exhaustive search over the same input.
    /// </summary>
    public class IdealLineupSolverPropertyTests
    {
        private static readonly string[] Codes =
            ["QB", "RB", "WR", "TE", "K", "DEF", "FLEX", "SUPER_FLEX", "REC_FLEX", "WRRB_FLEX"];
        private static readonly string[] Positions = ["QB", "RB", "WR", "TE", "K", "DEF"];

        private static decimal BruteForce(
            IReadOnlyList<RosteredPlayer> players, IReadOnlyList<Slot> slots)
        {
            var best = 0m;
            var used = new bool[players.Count];

            void Search(int slotIndex, decimal running)
            {
                if (running > best)
                {
                    best = running;
                }

                if (slotIndex >= slots.Count)
                {
                    return;
                }

                Search(slotIndex + 1, running);   // leaving a slot empty is allowed
                for (var i = 0; i < players.Count; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }

                    if (!slots[slotIndex].Accepts(players[i].FantasyPositions))
                    {
                        continue;
                    }

                    used[i] = true;
                    Search(slotIndex + 1, running + players[i].Points);
                    used[i] = false;
                }
            }

            Search(0, 0m);
            return best;
        }

        private static (List<RosteredPlayer> Players, IReadOnlyList<Slot> Slots) Random(Random rng)
        {
            var slots = Enumerable.Range(0, rng.Next(1, 7))
                .Select(_ => Codes[rng.Next(Codes.Length)])
                .ToArray();

            var players = Enumerable.Range(0, rng.Next(0, 9)).Select(i =>
            {
                var positions = Enumerable.Range(0, rng.Next(1, 3))
                    .Select(_ => Positions[rng.Next(Positions.Length)])
                    .Distinct()
                    .ToArray();
                return Roster.Player($"p{i}", Math.Round((decimal)(rng.NextDouble() * 30), 2), positions);
            }).ToList();

            return (players, Slot.Parse(slots));
        }

        [Fact]
        public void The_points_match_an_exhaustive_search_over_two_thousand_rosters()
        {
            var rng = new System.Random(20260910);

            for (var i = 0; i < 2000; i++)
            {
                var (players, slots) = Random(rng);

                IdealLineupSolver.Solve(players, slots).IdealPts
                    .Should().Be(BruteForce(players, slots), $"case {i} should match brute force");
            }
        }

        [Fact]
        public void The_lineup_is_consistent_over_two_thousand_rosters()
        {
            var rng = new System.Random(20260910);

            for (var i = 0; i < 2000; i++)
            {
                var (players, slots) = Random(rng);
                var (points, lineup) = IdealLineupSolver.Solve(players, slots);
                var byId = players.ToDictionary(p => p.Id);

                lineup.Should().OnlyHaveUniqueItems($"case {i} must not repeat a player");
                lineup.Count.Should().BeLessThanOrEqualTo(slots.Count, $"case {i}");
                lineup.Sum(id => byId[id].Points)
                      .Should().Be(points, $"case {i} lineup must account for the points returned");
            }
        }
    }

    /// <summary>
    /// The solver against the real captured week: the invariant that matters.
    /// </summary>
    public class IdealLineupSolverFixtureTests
    {
        private static JsonDocument Fixture(string name) =>
            JsonDocument.Parse(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "fixtures", name)));

        private static IReadOnlyList<Slot> LeagueSlots()
        {
            using var doc = Fixture("roster-positions.json");
            return Slot.Parse(doc.RootElement.GetProperty("roster_positions")
                                 .EnumerateArray().Select(e => e.GetString()!));
        }

        private static Dictionary<string, string[]> PlayerPositions()
        {
            using var doc = Fixture("player-positions.json");
            return doc.RootElement.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value.GetProperty("fantasyPositions")
                            .EnumerateArray().Select(e => e.GetString()!).ToArray());
        }

        /// <summary>
        /// Builds a roster from a matchup entry, failing loudly if any player is
        /// absent from the position map.
        /// </summary>
        /// <remarks>
        /// Skipping unresolved players would be the same silent-undercount bug the
        /// solver itself must avoid: rosters change on waivers, so a position map
        /// captured in week 1 goes stale, and a dropped player would quietly lower
        /// ideal with every test still green. Re-run
        /// <c>scripts/capture_fixtures.py</c> if this fires.
        /// </remarks>
        private static List<RosteredPlayer> Roster(
            JsonElement entry,
            Dictionary<string, string[]> positions,
            Func<string, decimal> points)
        {
            var ids = entry.GetProperty("players").EnumerateArray()
                .Select(e => e.GetString()!)
                .Where(id => id != "0")
                .ToList();

            var unresolved = ids.Where(id => !positions.ContainsKey(id)).ToList();
            unresolved.Should().BeEmpty(
                "every rostered player must appear in player-positions.json; " +
                "re-run scripts/capture_fixtures.py to refresh it");

            return ids
                .Select(id => new RosteredPlayer(new PlayerId(id), id, positions[id], points(id)))
                .ToList();
        }

        /// <summary>
        /// Every week captured by scripts/capture_fixtures.py, found on disk so a
        /// new capture is covered without editing this file.
        /// </summary>
        public static IEnumerable<object[]> CapturedWeeks() =>
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "wk*-matchups.json")
                .Select(path => new object[] { Path.GetFileName(path) })
                .OrderBy(row => (string)row[0]);

        [Theory]
        [MemberData(nameof(CapturedWeeks))]
        public void Ideal_is_never_below_actual_for_any_roster(string weekFile)
        {
            // The single assertion that catches nearly every way the solver can be
            // wrong: the lineup actually started is one of the assignments the
            // solver considers, so ideal can never come out lower.
            var slots = LeagueSlots();
            var positions = PlayerPositions();
            using var week = Fixture(weekFile);
            var gaps = 0;

            foreach (var entry in week.RootElement.EnumerateArray())
            {
                var rosterId = entry.GetProperty("roster_id").GetInt32();
                var actual = entry.GetProperty("points").GetDecimal();
                var scores = entry.GetProperty("players_points");

                // A player present in `players` but absent from `players_points`
                // scored zero; that is data, not an error.
                var players = Roster(entry, positions,
                    id => scores.TryGetProperty(id, out var v) ? v.GetDecimal() : 0m);

                var (ideal, lineup) = IdealLineupSolver.Solve(players, slots);

                ideal.Should().BeGreaterThanOrEqualTo(
                    actual, $"roster {rosterId} ideal must not fall below what it actually scored");
                lineup.Should().OnlyHaveUniqueItems($"roster {rosterId}");
                lineup.Count.Should().BeLessThanOrEqualTo(slots.Count, $"roster {rosterId}");

                if (ideal > actual)
                {
                    gaps++;
                }
            }

            // ideal >= actual alone would still pass if the solver just returned
            // the lineup the manager started. In a real week somebody always
            // leaves points on the bench, so demand at least one genuine gap (B5).
            gaps.Should().BeGreaterThan(0,
                $"in {weekFile} at least one roster should have left points on the bench");
        }

        [Fact]
        public void A_roster_short_at_a_position_leaves_that_slot_empty()
        {
            // Real partial fills, not a contrived bye week. In the captured week
            // roster 4 rosters no kicker and roster 5 no defence, so neither can
            // fill all eleven slots and dp[full] is genuinely unreachable for
            // them. Giving every player a point makes the total a slot count.
            var expected = new Dictionary<int, decimal>
            {
                [1] = 11m,
                [2] = 11m,
                [3] = 11m,
                [4] = 10m,            // no K on the roster
                [5] = 9m,             // no DEF, and short at RB
                [6] = 11m,
            };

            var slots = LeagueSlots();
            var positions = PlayerPositions();
            using var week = Fixture("wk1-matchups.json");

            foreach (var entry in week.RootElement.EnumerateArray())
            {
                var rosterId = entry.GetProperty("roster_id").GetInt32();
                var players = Roster(entry, positions, _ => 1m);

                IdealLineupSolver.Solve(players, slots).IdealPts
                    .Should().Be(expected[rosterId], $"roster {rosterId}");
            }
        }

        [Fact]
        public void Ideal_fills_at_least_as_many_slots_as_the_manager_did()
        {
            // The lineup-shaped cousin of ideal >= actual: whatever the manager
            // managed to start, the solver can seat at least that many. An empty
            // starter slot shows up as the string "0".
            var slots = LeagueSlots();
            var positions = PlayerPositions();
            using var week = Fixture("wk1-matchups.json");

            foreach (var entry in week.RootElement.EnumerateArray())
            {
                var rosterId = entry.GetProperty("roster_id").GetInt32();
                var started = entry.GetProperty("starters").EnumerateArray()
                    .Count(e => e.GetString() != "0");

                var players = Roster(entry, positions, _ => 1m);

                IdealLineupSolver.Solve(players, slots).IdealPts
                    .Should().BeGreaterThanOrEqualTo(started, $"roster {rosterId}");
            }
        }
    }
}
