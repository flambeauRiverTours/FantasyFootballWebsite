using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    public class FinalizeScheduleTests
    {
        private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
            new(year, month, day, hour, minute, 0, TimeSpan.Zero);

        [Fact]
        public void The_timer_runs_at_noon_on_Tuesdays_from_September_to_February()
        {
            // the reader's rule and the timer's expression must describe the same runs
            var fields = FinalizeSchedule.Cron.Split(' ');

            fields[2].Should().Be("12");
            fields[4].Should().Be("9,10,11,12,1,2");
            fields[5].Should().Be("2", "NCRONTAB counts Tuesday as 2");
        }

        [Fact]
        public void A_run_counts_from_the_moment_it_starts() =>
            FinalizeSchedule.LastRunAtOrBefore(Utc(2026, 10, 13, 12)).Should().Be(Utc(2026, 10, 13, 12));

        [Fact]
        public void A_minute_before_the_run_is_still_last_week() =>
            FinalizeSchedule.LastRunAtOrBefore(Utc(2026, 10, 13, 11, 59)).Should().Be(Utc(2026, 10, 6, 12));

        [Fact]
        public void Monday_looks_back_to_the_previous_Tuesday() =>
            FinalizeSchedule.LastRunAtOrBefore(Utc(2026, 10, 19, 23)).Should().Be(Utc(2026, 10, 13, 12));

        [Fact]
        public void The_offseason_looks_back_to_the_last_Tuesday_of_February() =>
            FinalizeSchedule.LastRunAtOrBefore(Utc(2027, 7, 4, 12)).Should().Be(Utc(2027, 2, 23, 12));

        [Fact]
        public void A_local_time_is_read_as_the_same_instant() =>
            FinalizeSchedule.LastRunAtOrBefore(new DateTimeOffset(2026, 10, 13, 8, 30, 0, TimeSpan.FromHours(-4)))
                .Should().Be(Utc(2026, 10, 13, 12));
    }
}
