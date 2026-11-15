using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Which weeks count (Q4, T64).
    /// </summary>
    public class RegularSeasonTests
    {
        [Fact]
        public void The_season_stops_the_week_before_the_playoffs() =>
            RegularSeason.LastWeek(15).Should().Be(14);

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        public void A_league_with_no_playoffs_counts_the_NFL_regular_season(int? playoffWeekStart) =>
            RegularSeason.LastWeek(playoffWeekStart).Should().Be(RegularSeason.NFLWeeks);

        [Fact]
        public void A_playoff_start_of_week_one_is_invalid_and_counts_the_NFL_season() =>
            // there would be no regular season at all; Sleeper does not allow it
            RegularSeason.LastWeek(1).Should().Be(RegularSeason.NFLWeeks);

        [Fact]
        public void A_playoff_start_past_the_NFL_season_is_capped_at_it() =>
            RegularSeason.LastWeek(21).Should().Be(RegularSeason.NFLWeeks);

        private static TeamWeek Week(int week, decimal actual) =>
            new(1, week, actual, actual, [], [], [], true, new Dictionary<string, decimal>(), null);

        [Fact]
        public void A_settled_week_nobody_scored_in_is_taken_off_the_end() =>
            // a league that ends at week 17 still gets a week 18 of zeros
            RegularSeason.LastCountedWeek(18, 18, [Week(17, 90m), Week(18, 0m), Week(18, 0m)]).Should().Be(17);

        [Fact]
        public void A_settled_week_with_no_matchups_at_all_is_taken_off_the_end_too() =>
            RegularSeason.LastCountedWeek(18, 18, [Week(16, 80m), Week(17, 90m)]).Should().Be(17);

        [Fact]
        public void A_week_anyone_scored_in_counts() =>
            RegularSeason.LastCountedWeek(18, 18, [Week(18, 0m), Week(18, 0.5m)]).Should().Be(18);

        [Fact]
        public void A_week_not_yet_settled_is_never_taken_off() =>
            // before kickoff every score is zero; the season is not over
            RegularSeason.LastCountedWeek(18, 17, [Week(17, 90m), Week(18, 0m)]).Should().Be(18);

        [Fact]
        public void A_season_nobody_scored_in_ends_at_week_one() =>
            RegularSeason.LastCountedWeek(18, 18, [Week(1, 0m), Week(2, 0m)]).Should().Be(1);

        [Fact]
        public void An_unplayed_week_in_the_middle_is_left_alone() =>
            // only the end is trimmed; the walk stops at the first week that was played
            RegularSeason.LastCountedWeek(18, 18, [Week(9, 0m), Week(18, 70m)]).Should().Be(18);
    }
}
