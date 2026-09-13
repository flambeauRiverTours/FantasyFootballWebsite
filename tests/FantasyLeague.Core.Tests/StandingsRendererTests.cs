using System.Globalization;
using FantasyLeague.Console;
using FantasyLeague.Sleeper;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    public class StandingsRendererTests
    {
        private static readonly DateTimeOffset AsOf = new(2026, 10, 18, 18, 42, 0, TimeSpan.Zero);

        private static TeamSeason Team(string name, int rosterId, params (decimal Actual, decimal Ideal, bool IsFinal)[] weeks) =>
            new(rosterId, name,
                [.. weeks.Select((w, i) => new TeamWeek(rosterId, i + 1, w.Actual, w.Ideal, [], [], w.IsFinal))]);

        private static SeasonSnapshot Snapshot(
            IReadOnlyList<TeamSeason>? teams = null,
            int currentWeek = 3,
            int finalThroughWeek = 2,
            IReadOnlyList<string>? unresolved = null,
            IReadOnlyList<InvariantViolation>? violations = null) =>
            new(2026, currentWeek, finalThroughWeek, Slot.Parse(["QB", "RB", "FLEX"]),
                teams ?? [], unresolved ?? [], violations ?? [], new Dictionary<int, string>());

        private static string Render(SeasonSnapshot snapshot)
        {
            var writer = new StringWriter();
            StandingsRenderer.Render(snapshot, writer, AsOf);
            return writer.ToString();
        }

        private static string[] Lines(string output) =>
            output.Split(Environment.NewLine, StringSplitOptions.None);

        [Fact]
        public void The_header_names_the_season_and_both_week_numbers()
        {
            var output = Render(Snapshot(currentWeek: 7, finalThroughWeek: 6));

            Lines(output)[0].Should().Be(
                "season 2026   weeks 1-7   final through wk 6   2026-10-18 18:42Z");
        }

        [Fact]
        public void Teams_are_ordered_by_points_descending()
        {
            var output = Render(Snapshot([
                Team("Third", 3, (10m, 20m, true)),
                Team("First", 1, (30m, 40m, true)),
                Team("Second", 2, (20m, 30m, true)),
            ]));

            var names = Lines(output).Skip(4).Take(3).Select(l => l.Split(' ')[0]);
            names.Should().Equal("First", "Second", "Third");
        }

        [Fact]
        public void The_leader_has_a_dash_for_the_gap_and_everyone_else_a_negative()
        {
            var output = Render(Snapshot([
                Team("Leader", 1, (100m, 100m, true)),
                Team("Chaser", 2, (87.5m, 100m, true)),
            ]));

            var lines = Lines(output);
            lines[4].Should().EndWith("-");
            lines[5].Should().EndWith("-12.5");
        }

        [Fact]
        public void Sigma_is_a_dash_below_four_finalized_weeks()
        {
            var output = Render(Snapshot([
                Team("Too Early", 1, (10m, 10m, true), (20m, 20m, true), (30m, 30m, true)),
            ]));

            Lines(output)[4].Should().Contain("      -");
        }

        [Fact]
        public void Sigma_appears_once_four_weeks_are_final()
        {
            var output = Render(Snapshot([
                Team("Steady", 1,
                    (8m, 8m, true), (12m, 12m, true), (8m, 8m, true), (12m, 12m, true)),
            ]));

            Lines(output)[4].Should().Contain("2.0");
        }

        [Fact]
        public void An_unfinished_week_counts_toward_points_but_not_sigma()
        {
            var output = Render(Snapshot([
                Team("Mid Sunday", 1,
                    (8m, 8m, true), (12m, 12m, true), (8m, 8m, true), (12m, 12m, true),
                    (41m, 41m, false)),
            ]));

            var row = Lines(output)[4];
            row.Should().Contain("81.0");
            row.Should().Contain("2.0");
        }

        [Fact]
        public void Efficiency_is_points_over_ideal()
        {
            var output = Render(Snapshot([
                Team("Left Some Out", 1, (35.2m, 42.0m, true)),
            ]));

            Lines(output)[4].Should().Contain("84%");
        }

        [Fact]
        public void A_team_that_scored_nothing_has_no_efficiency()
        {
            var output = Render(Snapshot([Team("Blanked", 1, (0m, 0m, true))]));

            Lines(output)[4].Should().Contain("     -");
        }

        [Fact]
        public void A_long_name_is_truncated_so_the_columns_cannot_move()
        {
            var output = Render(Snapshot([
                Team("A Team With A Really Very Long Name", 1, (10m, 10m, true)),
                Team("Short", 2, (5m, 5m, true)),
            ]));

            var lines = Lines(output);
            lines[4].Should().StartWith("A Team With A Really Ver~");
            lines[4].Length.Should().Be(lines[5].Length);
        }

        [Fact]
        public void Every_row_is_as_wide_as_the_rule()
        {
            var output = Render(Snapshot([
                Team("Aaron Donald Relief Fund", 1, (812.66m, 941.2m, true)),
                Team("LilDab", 2, (9.9m, 14.0m, true)),
            ]));

            var lines = Lines(output);
            var rule = lines[3].Length;

            lines[2].Length.Should().Be(rule);
            lines[4].Length.Should().Be(rule);
            lines[5].Length.Should().Be(rule);
        }

        [Fact]
        public void A_clean_season_reports_OK_with_a_team_week_count()
        {
            var output = Render(Snapshot([
                Team("One", 1, (10m, 10m, true), (10m, 10m, true)),
                Team("Two", 2, (10m, 10m, true), (10m, 10m, true)),
            ]));

            output.Should().Contain("starters==points  OK   (4 team-weeks)");
            output.Should().Contain("ideal>=points     OK   (4 team-weeks)");
            output.Should().Contain("unresolved   none");
        }

        [Fact]
        public void A_violation_flips_only_its_own_invariant_to_FAIL()
        {
            var output = Render(Snapshot(
                [Team("One", 1, (10m, 10m, true))],
                violations: [new InvariantViolation(1, 1, InvariantKind.IdealNotBelowActual, 40m, 35m)]));

            output.Should().Contain("starters==points  OK");
            output.Should().Contain("ideal>=points     FAIL");
        }

        [Fact]
        public void A_violation_is_printed_with_its_week_and_roster()
        {
            var output = Render(Snapshot(
                [Team("One", 1, (10m, 10m, true))],
                violations: [new InvariantViolation(6, 3, InvariantKind.StartersSumMatchesPoints, 121.44m, 118.2m)]));

            output.Should().Contain("wk 6");
            output.Should().Contain("roster 3");
            output.Should().Contain("121.44");
            output.Should().Contain("118.20");
        }

        [Fact]
        public void Violations_are_ordered_by_week_then_roster()
        {
            var output = Render(Snapshot(
                [Team("One", 1, (10m, 10m, true))],
                violations: [
                    new InvariantViolation(3, 2, InvariantKind.IdealNotBelowActual, 1m, 0m),
                    new InvariantViolation(1, 5, InvariantKind.IdealNotBelowActual, 1m, 0m),
                    new InvariantViolation(1, 2, InvariantKind.IdealNotBelowActual, 1m, 0m),
                ]));

            var detail = Lines(output).Where(l => l.StartsWith("  wk ")).ToList();
            detail[0].Should().Contain("wk 1").And.Contain("roster 2");
            detail[1].Should().Contain("wk 1").And.Contain("roster 5");
            detail[2].Should().Contain("wk 3").And.Contain("roster 2");
        }

        [Fact]
        public void Unresolved_players_are_listed_rather_than_counted()
        {
            var output = Render(Snapshot(
                [Team("One", 1, (10m, 10m, true))],
                unresolved: ["signed_tuesday", "99999"]));

            output.Should().Contain("unresolved   signed_tuesday, 99999");
        }

        [Fact]
        public void A_season_with_no_teams_still_renders()
        {
            var output = Render(Snapshot());

            output.Should().Contain("TEAM");
            output.Should().Contain("(0 team-weeks)");
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        [InlineData("en-US")]
        public void The_output_does_not_change_with_the_ambient_culture(string culture)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);

                var output = Render(Snapshot([
                    Team("Decimal Heavy", 1,
                        (8.5m, 9.25m, true), (12.75m, 13m, true), (8m, 8m, true), (12m, 12m, true)),
                ]));

                output.Should().Contain("41.3", "a comma-decimal culture would render 41,3");
                output.Should().Contain("42.3");
                output.Should().Contain("98%");
                output.Should().NotContain(",");
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
