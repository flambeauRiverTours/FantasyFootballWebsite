using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// The absence cases. Almost all of T08 is about weeks that are not there:
    /// each input should give a defined result for a stated reason, and none
    /// should throw.
    /// </summary>
    public class SpreadCalculatorAbsenceTests
    {
        [Fact]
        public void No_weeks_at_all_is_null() =>
            // Week zero, before any games have been played.
            SpreadCalculator.Calculate([]).Should().BeNull();

        [Fact]
        public void One_week_is_null_not_zero() =>
            // Sigma of a single point is undefined, not zero. Returning 0.0 here
            // would put the newest, least-known team top of a consistency table.
            SpreadCalculator.Calculate([118.2m]).Should().BeNull();

        [Fact]
        public void Three_weeks_is_null() =>
            // The minimum-weeks boundary, approached from below.
            SpreadCalculator.Calculate([118.2m, 96.4m, 131.0m]).Should().BeNull();

        [Fact]
        public void Four_weeks_is_a_number()
        {
            // The same boundary, from above.
            var spread = SpreadCalculator.Calculate([118.2m, 96.4m, 131.0m, 104.8m]);

            spread.Should().NotBeNull();
            spread!.Weeks.Should().Be(4);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Every_count_below_the_minimum_is_null(int weeks)
        {
            var input = Enumerable.Range(0, weeks).Select(i => 100m + i).ToList();

            SpreadCalculator.Calculate(input).Should().BeNull();
        }

        [Fact]
        public void The_minimum_is_configurable_because_Q5_is_still_open()
        {
            // Four or five is undecided; the parameter is what keeps that a
            // one-line change rather than a hunt.
            IReadOnlyList<decimal> fourWeeks = [118.2m, 96.4m, 131.0m, 104.8m];

            SpreadCalculator.Calculate(fourWeeks, minWeeks: 4).Should().NotBeNull();
            SpreadCalculator.Calculate(fourWeeks, minWeeks: 5).Should().BeNull();
        }
    }

    /// <summary>
    /// A zero is not the same thing as a missing week.
    /// </summary>
    public class SpreadCalculatorZeroTests
    {
        [Fact]
        public void Four_identical_weeks_give_a_genuine_zero()
        {
            // Perfect consistency exists, and 0.0 is the honest answer for it.
            // This is the one case where sigma may legitimately be zero.
            var spread = SpreadCalculator.Calculate([100m, 100m, 100m, 100m]);

            spread.Should().NotBeNull();
            spread!.Sigma.Should().Be(0.0);
            spread.Mean.Should().Be(100m);
        }

        [Fact]
        public void A_week_that_really_scored_nothing_is_data_not_a_gap()
        {
            // A 0.00 week counts toward the spread like any other. Dropping it
            // would flatter a team that was blanked.
            var spread = SpreadCalculator.Calculate([0m, 120m, 110m, 115m]);

            spread.Should().NotBeNull();
            spread!.Weeks.Should().Be(4);
            spread.Min.Should().Be(0m);
            spread.Mean.Should().Be(86.25m);
            spread.Sigma.Should().BeApproximately(49.921814, 1e-6);
        }

        [Fact]
        public void All_zero_weeks_do_not_divide_by_zero()
        {
            // Mean is zero, so the coefficient of variation has no denominator.
            // Sigma is also zero here, so reporting a CV of zero is honest.
            var spread = SpreadCalculator.Calculate([0m, 0m, 0m, 0m]);

            spread.Should().NotBeNull();
            spread!.Sigma.Should().Be(0.0);
            spread.Cv.Should().Be(0.0);
        }
    }

    /// <summary>
    /// The definition itself: population sigma, and the summary figures the
    /// table's inline range mark needs.
    /// </summary>
    public class SpreadCalculatorDefinitionTests
    {
        // mean 10, deviations -2/+2/-2/+2. Population sigma is exactly 2.0;
        // Bessel's correction would give sqrt(16/3) = 2.3094. Chosen so the two
        // definitions cannot be confused by a rounding tolerance.
        private static readonly decimal[] Symmetric = [8m, 12m, 8m, 12m];

        [Fact]
        public void Sigma_is_the_population_deviation_not_the_sample_one()
        {
            // D1: the weeks played are the complete population, not a sample
            // drawn from some larger set of hypothetical weeks.
            var spread = SpreadCalculator.Calculate(Symmetric);

            spread!.Sigma.Should().Be(2.0);
            spread.Sigma.Should().NotBeApproximately(2.3094011, 1e-4,
                "dividing by n-1 instead of n would give this");
        }

        [Fact]
        public void Cv_is_sigma_over_the_mean() => SpreadCalculator.Calculate(Symmetric)!.Cv.Should().BeApproximately(0.2, 1e-12);

        [Fact]
        public void Mean_min_max_and_weeks_describe_the_input()
        {
            // These four exist so the table can draw its range mark without
            // recomputing anything sigma already walked.
            var spread = SpreadCalculator.Calculate([121.44m, 98.2m, 140.6m, 110m, 133.3m]);

            spread.Should().NotBeNull();
            spread!.Weeks.Should().Be(5);
            spread.Mean.Should().Be(120.708m);
            spread.Min.Should().Be(98.2m);
            spread.Max.Should().Be(140.6m);
            spread.Sigma.Should().BeApproximately(15.336605, 1e-6);
        }

        [Fact]
        public void The_order_of_the_weeks_does_not_matter()
        {
            IReadOnlyList<decimal> ordered = [98.2m, 110m, 121.44m, 133.3m, 140.6m];
            IReadOnlyList<decimal> shuffled = [140.6m, 98.2m, 133.3m, 121.44m, 110m];

            SpreadCalculator.Calculate(shuffled)!.Sigma
                .Should().Be(SpreadCalculator.Calculate(ordered)!.Sigma);
        }

        [Fact]
        public void A_negative_weekly_total_is_included_rather_than_rejected()
        {
            // Sleeper scoring can take a team below zero. Whether this league's
            // settings allow it is Q4-adjacent, but the maths must not care.
            var spread = SpreadCalculator.Calculate([-4.5m, 88m, 102m, 95m]);

            spread.Should().NotBeNull();
            spread!.Min.Should().Be(-4.5m);
            spread.Sigma.Should().BeGreaterThan(0);
        }
    }

    /// <summary>
    /// R1: an in-progress week must never reach the calculation.
    /// </summary>
    public class SpreadCalculatorFinalityTests
    {
        private static TeamWeek Week(int week, decimal actual, bool isFinal) =>
            new(RosterId: 1, Week: week, Actual: actual, Ideal: actual,
                Starters: [], IdealLineup: [], Players: [], IsFinal: isFinal,
                SlotAverages: new Dictionary<string, decimal>());

        [Fact]
        public void An_unfinished_week_is_excluded_not_zeroed()
        {
            // The row that will actually break in production. A team sitting on
            // 41 points at 2pm Sunday is not a catastrophic outlier, it is a
            // week that has not happened yet -- and letting it through makes
            // sigma lurch by tens of points and then fall back over the
            // afternoon, so the standings churn while nothing is happening.
            TeamWeek[] season =
            [
                Week(1, 8m, isFinal: true),
                Week(2, 12m, isFinal: true),
                Week(3, 8m, isFinal: true),
                Week(4, 12m, isFinal: true),
                Week(5, 41m, isFinal: false),   // Sunday afternoon, still playing
            ];

            var spread = SpreadCalculator.Calculate(
                [.. season.Where(w => w.IsFinal).Select(w => w.Actual)]);

            spread.Should().NotBeNull();
            spread!.Weeks.Should().Be(4, "the in-progress week must not be counted");
            spread.Sigma.Should().Be(2.0, "sigma covers weeks 1-4 only");
            spread.Max.Should().Be(12m, "41 belongs to a week that has not finished");
        }

        [Fact]
        public void Zeroing_an_unfinished_week_would_give_a_different_answer()
        {
            // Guards against the tempting alternative of defaulting the
            // in-progress week to 0 rather than dropping it.
            var excluded = SpreadCalculator.Calculate([8m, 12m, 8m, 12m])!;
            var zeroed = SpreadCalculator.Calculate([8m, 12m, 8m, 12m, 0m])!;

            zeroed.Sigma.Should().NotBe(excluded.Sigma);
            zeroed.Weeks.Should().Be(5);
        }

        [Fact]
        public void A_season_with_too_few_finalized_weeks_is_still_null()
        {
            // Three final weeks plus two in progress is three weeks of data, and
            // the column shows an em dash.
            TeamWeek[] season =
            [
                Week(1, 118.2m, isFinal: true),
                Week(2, 96.4m, isFinal: true),
                Week(3, 131.0m, isFinal: true),
                Week(4, 44.0m, isFinal: false),
                Week(5, 12.0m, isFinal: false),
            ];

            SpreadCalculator.Calculate(
                [.. season.Where(w => w.IsFinal).Select(w => w.Actual)])
                .Should().BeNull();
        }
    }
}
