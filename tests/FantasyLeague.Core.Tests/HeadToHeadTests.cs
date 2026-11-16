using System.Text.Json;
using FantasyLeague.Sleeper;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Head-to-head records (T68, Q12).
    /// </summary>
    public class HeadToHeadTests
    {
        private static TeamWeek Week(int rosterId, int week, decimal points, int? matchupId, bool isFinal = true) =>
            new(rosterId, week, points, points, [], [], [], isFinal, new Dictionary<string, decimal>(), matchupId);

        /// <summary>
        /// Every captured week, mapped as the season service maps it: each roster's
        /// score (any commissioner override applied) and its game.
        /// </summary>
        private static IEnumerable<TeamWeek> CapturedWeeks() =>
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "wk*-matchups.json")
                .SelectMany(path =>
                {
                    var week = int.Parse(Path.GetFileName(path)[2..^"-matchups.json".Length]);
                    return Fixtures.Read<List<MatchupDto>>(File.ReadAllText(path))
                        .Select(m => SleeperMapper.ToRosterWeek(m, new Dictionary<string, PlayerRef>()))
                        .Select(r => Week(r.RosterId, week, r.ActualPts, r.MatchupId));
                });

        [Fact]
        public void The_captured_season_matches_Sleepers_own_records()
        {
            // the real league: a median game, and three games a week, one against the placeholder
            using var document = JsonDocument.Parse(Fixtures.Text("rosters-record.json"));
            var sleepers = Fixtures.Read<List<RosterDto>>(document.RootElement.GetProperty("rosters").GetRawText())
                .ToDictionary(r => r.RosterId, r => SleeperMapper.ToRecord(r)!);

            HeadToHead.Records(CapturedWeeks(), lastWeek: 18, hasMedianGame: true).Should().BeEquivalentTo(sleepers);
        }

        [Fact]
        public void The_higher_score_wins_and_both_scores_are_kept()
        {
            var records = HeadToHead.Records([Week(1, 1, 100m, 1), Week(2, 1, 90m, 1)], 18, hasMedianGame: false)!;

            records[1].Should().Be(new TeamRecord(1, 0, 0, 100m, 90m));
            records[2].Should().Be(new TeamRecord(0, 1, 0, 90m, 100m));
        }

        [Fact]
        public void Equal_scores_tie() =>
            HeadToHead.Records([Week(1, 1, 90m, 1), Week(2, 1, 90m, 1)], 18, hasMedianGame: false)![1]
                .Should().Be(new TeamRecord(0, 0, 1, 90m, 90m));

        [Fact]
        public void A_bye_is_no_game_but_its_points_count() =>
            // points for covers every counted week, as Sleeper's fpts and the Ideal column do
            HeadToHead.Records([Week(1, 1, 100m, 1), Week(2, 1, 90m, 1), Week(3, 1, 80m, 0)], 18, hasMedianGame: false)![3]
                .Should().Be(new TeamRecord(0, 0, 0, 80m, 0m));

        [Fact]
        public void The_median_game_adds_a_result_against_the_week_s_median()
        {
            // median of 100, 90, 80 and 70 is 85: the top two win it, the bottom two lose it
            var records = HeadToHead.Records(
                [Week(1, 1, 100m, 1), Week(2, 1, 70m, 1), Week(3, 1, 90m, 2), Week(4, 1, 80m, 2)], 18, hasMedianGame: true)!;

            records[2].Should().Be(new TeamRecord(0, 2, 0, 70m, 100m));
            records[4].Should().Be(new TeamRecord(0, 2, 0, 80m, 90m));
            records[3].Should().Be(new TeamRecord(2, 0, 0, 90m, 80m));
        }

        [Fact]
        public void Weeks_past_the_last_counted_week_do_not_count() =>
            // a trimmed week is a 0-0 game, which would otherwise count as a tie (A37)
            HeadToHead.Records([Week(1, 1, 100m, 1), Week(2, 1, 90m, 1), Week(1, 2, 0m, 1), Week(2, 2, 0m, 1)], 1, hasMedianGame: false)![1]
                .Should().Be(new TeamRecord(1, 0, 0, 100m, 90m));

        [Fact]
        public void The_live_week_does_not_count() =>
            HeadToHead.Records([Week(1, 1, 100m, 1), Week(2, 1, 90m, 1), Week(1, 2, 10m, 1, isFinal: false), Week(2, 2, 50m, 1, isFinal: false)], 18, hasMedianGame: true)![1]
                .Should().Be(new TeamRecord(2, 0, 0, 100m, 90m));

        [Fact]
        public void A_week_stored_before_matchups_were_read_leaves_every_record_unknown() =>
            HeadToHead.Records([Week(1, 1, 100m, 1), Week(2, 1, 90m, 1), Week(1, 2, 100m, null), Week(2, 2, 90m, 1)], 18, hasMedianGame: false)
                .Should().BeNull("a partial record would rank teams wrongly");

        [Fact]
        public void A_game_that_does_not_pair_two_rosters_leaves_every_record_unknown() =>
            HeadToHead.Records([Week(1, 1, 100m, 1), Week(2, 1, 90m, 1), Week(3, 1, 80m, 1)], 18, hasMedianGame: false)
                .Should().BeNull();

        [Fact]
        public void Games_come_by_week_then_matchup_with_each_game_s_sides_in_roster_order()
        {
            // stable between polls, so a change of lead never swaps a card's rows (T71)
            var games = HeadToHead.Games([
                Week(4, 2, 10m, 1), Week(2, 2, 20m, 1),
                Week(3, 1, 30m, 2), Week(1, 1, 40m, 2),
                Week(4, 1, 50m, 1), Week(2, 1, 60m, 1),
            ])!;

            games.Select(g => (g.First.Week, g.First.MatchupId, g.First.RosterId, g.Second.RosterId))
                .Should().Equal((1, 1, 2, 4), (1, 2, 1, 3), (2, 1, 2, 4));
        }

        [Fact]
        public void A_bye_is_not_a_game() =>
            HeadToHead.Games([Week(1, 1, 100m, 1), Week(2, 1, 90m, 1), Week(3, 1, 80m, 0)])!
                .Select(g => (g.First.RosterId, g.Second.RosterId)).Should().Equal((1, 2));

        [Fact]
        public void A_week_with_no_game_has_none() =>
            // an unscheduled week is stored as all byes: no games, never an error
            HeadToHead.Games([Week(1, 18, 0m, 0), Week(2, 18, 0m, 0)]).Should().NotBeNull().And.BeEmpty();
    }
}
