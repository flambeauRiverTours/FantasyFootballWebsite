using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Rounds to weeks, and when a playoff match is settled (T69, A46).
    /// </summary>
    public class PlayoffsTests
    {
        private static PlayoffMatch Match(int round, int match, int? winner = null, int? placement = null) =>
            new(round, match, new BracketSide(1, BracketSource.None, 0), new BracketSide(2, BracketSource.None, 0), winner, placement);

        [Theory]
        [InlineData(0, 1, new[] { 15 })]
        [InlineData(0, 3, new[] { 17 })]
        [InlineData(1, 2, new[] { 16 })]
        [InlineData(1, 3, new[] { 17, 18 })] // the final spans two weeks
        [InlineData(2, 1, new[] { 15, 16 })]
        [InlineData(2, 2, new[] { 17, 18 })]
        public void Each_round_type_maps_its_rounds_to_weeks(int roundType, int round, int[] weeks) =>
            Playoffs.RoundWeeks(15, roundType, round, rounds: 3).Should().Equal(weeks);

        [Fact]
        public void An_unknown_round_type_maps_to_nothing() =>
            Playoffs.RoundWeeks(15, 7, 1, rounds: 3).Should().BeNull();

        [Fact]
        public void A_bracket_maps_every_round()
        {
            var weeks = Playoffs.Weeks([Match(1, 1), Match(2, 2), Match(3, 3, placement: 1)], 15, 0)!;

            weeks.Should().BeEquivalentTo(new Dictionary<int, IReadOnlyList<int>> { [1] = [15], [2] = [16], [3] = [17] });
        }

        [Fact]
        public void A_round_past_week_18_drops_the_whole_bracket() =>
            // type 2 from week 16: round 2 would be weeks 18 and 19
            Playoffs.Weeks([Match(1, 1), Match(2, 2, placement: 1)], 16, 2).Should().BeNull();

        [Fact]
        public void An_empty_bracket_maps_to_nothing() =>
            Playoffs.Weeks([], 15, 0).Should().BeNull();

        [Fact]
        public void A_match_is_settled_once_its_weeks_are_final_and_Sleeper_named_a_winner()
        {
            Playoffs.IsSettled(Match(1, 1, winner: 1), [15, 16], finalThroughWeek: 16).Should().BeTrue();
            Playoffs.IsSettled(Match(1, 1, winner: 1), [15, 16], finalThroughWeek: 15).Should().BeFalse("its second week is live");
            Playoffs.IsSettled(Match(1, 1), [15, 16], finalThroughWeek: 16).Should().BeFalse("Sleeper has not settled it yet");
        }

        [Fact]
        public void A_match_ending_in_week_18_is_settled_once_Sleeper_names_its_winner() =>
            // Sleeper's state after week 18 is unobserved (A37), so week 18 may never be final by the state
            Playoffs.IsSettled(Match(2, 1, winner: 2), [18], finalThroughWeek: 17).Should().BeTrue();
    }
}
