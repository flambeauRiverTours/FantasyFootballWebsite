using System.Globalization;
using FantasyLeague.Cli;
using FantasyLeague.Seasons;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    public class StandingsRendererTests
    {
        private static readonly DateTimeOffset AsOf = new(2026, 10, 18, 18, 42, 0, TimeSpan.Zero);

        private static TeamSeason Team(string name, int rosterId, params (decimal Actual, decimal Ideal, bool IsFinal)[] weeks) =>
            new(rosterId, name,
                [.. weeks.Select((w, i) => new TeamWeek(rosterId, i + 1, w.Actual, w.Ideal, [], [], [], w.IsFinal, new Dictionary<string, decimal>(), null))]);

        private static TeamSeason InGames(string name, int rosterId, params (decimal Actual, int MatchupId)[] weeks) =>
            new(rosterId, name,
                [.. weeks.Select((w, i) => new TeamWeek(rosterId, i + 1, w.Actual, w.Actual, [], [], [], true, new Dictionary<string, decimal>(), w.MatchupId))]);

        private static SeasonSnapshot Snapshot(
            IReadOnlyList<TeamSeason>? teams = null,
            int currentWeek = 3,
            int finalThroughWeek = 2,
            IReadOnlyList<string>? unresolved = null,
            IReadOnlyList<InvariantViolation>? violations = null,
            int cutoff = RegularSeason.NFLWeeks,
            LeagueFormat format = LeagueFormat.TotalPoints,
            RecordCheck? recordCheck = null) =>
            new("Name", 2026, currentWeek, finalThroughWeek, Slot.Parse(["QB", "RB", "FLEX"]),
                teams ?? [], unresolved ?? [], violations ?? [], IsCurrent: true, cutoff, format, [], false, recordCheck);

        // one settled week: Alpha beats Bravo 100-10, Delta beats Charlie 6-5. By points Bravo
        // is second; by record Delta is, on win rate, then points for (T72a)
        private static SeasonSnapshot HeadToHeadWeek(RecordCheck? recordCheck = null) =>
            Snapshot([
                InGames("Alpha", 1, (100m, 1)),
                InGames("Bravo", 2, (10m, 1)),
                InGames("Charlie", 3, (5m, 2)),
                InGames("Delta", 4, (6m, 2)),
            ], currentWeek: 2, finalThroughWeek: 1, format: LeagueFormat.HeadToHead, recordCheck: recordCheck);

        private static string[] Row(string output, string name) =>
            Lines(output).Single(l => l.StartsWith(name + " ", StringComparison.Ordinal)).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        private static string Render(SeasonSnapshot snapshot)
        {
            var writer = new StringWriter();
            StandingsRenderer.Render(SeasonPayloadMapper.ToSeasonPayload(snapshot, AsOf), snapshot, writer);
            return writer.ToString();
        }

        private static string[] Lines(string output) =>
            output.Split(Environment.NewLine, StringSplitOptions.None);

        [Fact]
        public void In_head_to_head_teams_rank_by_record_with_games_back_and_points_for_and_against()
        {
            var output = Render(HeadToHeadWeek());

            Lines(output)[2].Should().Be($"{"TEAM",-26}{"RECORD",9}{"GB",6}{"PF",9}{"PA",8}{"IDEAL",8}");
            Lines(output).Skip(4).Take(4).Select(l => l.Split(' ')[0]).Should().Equal("Alpha", "Delta", "Bravo", "Charlie");
            Row(output, "Alpha").Should().Equal("Alpha", "1-0", "-", "100.0", "10.0", "100.0");
            Row(output, "Charlie").Should().Equal("Charlie", "0-1", "1.0", "5.0", "6.0", "5.0");
            Lines(output).Skip(3).Take(6).Should().OnlyContain(l => l.Length == 66);
        }

        [Fact]
        public void The_placeholder_s_record_is_neither_a_row_nor_the_mark_games_back_counts_from()
        {
            // A1: the placeholder beats Alpha twice (2-0, the best margin of anyone); Bravo and
            // Charlie split, so the table's best margin is theirs, 0, and Charlie is no games back
            var snapshot = Snapshot([
                InGames("Alpha", 1, (100m, 1), (100m, 1)),
                InGames("Bravo", 2, (10m, 2), (30m, 2)),
                InGames("Charlie", 3, (20m, 2), (5m, 2)),
            ], currentWeek: 3, finalThroughWeek: 2, format: LeagueFormat.HeadToHead) with
            {
                ExcludedTeams = [InGames("Placeholder", 9, (200m, 1), (200m, 1))],
            };

            var output = Render(snapshot);

            output.Should().NotContain("Placeholder");
            Row(output, "Charlie")[2].Should().Be("-");
            Row(output, "Alpha")[2].Should().Be("1.0");
        }

        [Fact]
        public void Head_to_head_keeps_the_points_table_until_every_record_is_known()
        {
            // no matchup ids: the records are unknown, as on the page (hasRecords)
            var output = Render(Snapshot([Team("Only", 1, (10m, 20m, true))], format: LeagueFormat.HeadToHead));

            Lines(output)[2].Should().StartWith("TEAM").And.Contain("POINTS");
        }

        [Fact]
        public void A_clean_record_check_says_how_many_rosters_it_compared() =>
            Render(HeadToHeadWeek(new RecordCheck(4, 0, []))).Should().Contain(
                $"record check OK   (4 rosters compared){Environment.NewLine}");

        [Fact]
        public void A_record_check_says_how_many_rosters_it_could_not_compare() =>
            Render(HeadToHeadWeek(new RecordCheck(3, 1, []))).Should().Contain(
                "record check OK   (3 rosters compared; 1 not, Sleeper has counted a different number of games)");

        [Fact]
        public void A_record_check_that_compared_nothing_is_not_OK() =>
            Render(HeadToHeadWeek(new RecordCheck(0, 4, []))).Should().Contain(
                "record check not run (0 rosters compared; 4 not, Sleeper has counted a different number of games)");

        [Fact]
        public void A_record_check_that_could_not_run_says_so() =>
            Render(HeadToHeadWeek()).Should().Contain("record check not run (records incomplete)");

        [Fact]
        public void A_record_mismatch_fails_and_is_printed_with_both_records()
        {
            var mismatch = new RecordMismatch(2, new TeamRecord(13, 5, 0, 0m, 0m), new TeamRecord(12, 6, 0, 0m, 0m));
            var output = Render(HeadToHeadWeek(new RecordCheck(4, 0, [mismatch])));

            output.Should().Contain("record check FAIL (1 of 4 rosters compared disagree)");
            output.Should().Contain("  roster 2   ours 13-5-0   sleeper 12-6-0");
        }

        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(1, 0, true)]
        [InlineData(0, 1, true)]
        public void The_exit_code_fails_on_a_violation_or_a_record_mismatch(int violations, int mismatches, bool failed)
        {
            var snapshot = HeadToHeadWeek(new RecordCheck(4, 0, [.. Enumerable.Repeat(new RecordMismatch(2, new(1, 0, 0, 0m, 0m), new(0, 1, 0, 0m, 0m)), mismatches)]))
                with
            { Violations = [.. Enumerable.Repeat(new InvariantViolation(1, 1, InvariantKind.IdealNotBelowActual, 10m, 9m), violations)] };

            StandingsRenderer.Failed(snapshot).Should().Be(failed);
        }

        [Fact]
        public void The_header_names_the_season_and_both_week_numbers()
        {
            var output = Render(Snapshot(currentWeek: 7, finalThroughWeek: 6));

            Lines(output)[0].Should().Be(
                "season 2026   TotalPoints   weeks 1-7   final through wk 6   2026-10-18 18:42Z");
        }

        [Fact]
        public void In_the_playoffs_the_header_names_the_weeks_counted_and_the_checks_say_what_they_cover()
        {
            var output = Render(Snapshot(
                [Team("Only", 1, (10m, 20m, true), (10m, 20m, true), (50m, 60m, false))],
                currentWeek: 3, finalThroughWeek: 2, cutoff: 2));

            Lines(output)[0].Should().Be(
                "season 2026   TotalPoints   weeks 1-2   final through wk 2   2026-10-18 18:42Z   playoffs from wk 3 not counted");
            output.Should().Contain("(3 team-weeks, playoffs included)");
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
