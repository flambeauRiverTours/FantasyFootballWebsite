using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    public class WeekFinalityTests
    {
        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 1)]
        [InlineData(18, 17)]
        public void Every_week_before_the_current_one_is_final(int currentWeek, int finalThrough) => WeekFinality.FinalThroughWeek(currentWeek).Should().Be(finalThrough);

        [Fact]
        public void Before_the_season_nothing_is_final() => WeekFinality.FinalThroughWeek(0).Should().Be(0);

        [Theory]
        [InlineData(6, 7, true)]
        [InlineData(7, 7, false)]
        [InlineData(8, 7, false)]
        public void A_week_is_final_once_sleeper_has_moved_past_it(int week, int currentWeek, bool expected) => WeekFinality.IsFinal(week, currentWeek).Should().Be(expected);
    }
}
