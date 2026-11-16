using FantasyLeague.Seasons;
using FantasyLeague.Sleeper;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// The third check: our head-to-head record against Sleeper's (T68), counted so a clean run can be told from no run (T72a).
    /// </summary>
    public class RecordCheckTests
    {
        private static Dictionary<int, SleeperTeam> Sleepers(TeamRecord? record) => new() { [1] = new("One", "u1", record) };

        private static Dictionary<int, TeamRecord> Ours(TeamRecord record) => new() { [1] = record };

        [Fact]
        public void Agreeing_records_are_compared_and_clean()
        {
            var check = RecordCheck.Run(Ours(new(13, 5, 0, 1m, 2m)), Sleepers(new(13, 5, 0, 9m, 9m)))!;

            check.Compared.Should().Be(1);
            check.Skipped.Should().Be(0);
            check.Mismatches.Should().BeEmpty("points are not compared: a later stat correction moves only Sleeper's");
        }

        [Fact]
        public void A_different_result_over_the_same_games_is_a_mismatch() =>
            RecordCheck.Run(Ours(new(13, 5, 0, 0m, 0m)), Sleepers(new(12, 6, 0, 0m, 0m)))!
                .Mismatches.Should().ContainSingle().Which.RosterId.Should().Be(1);

        [Fact]
        public void Sleeper_a_week_behind_is_skipped_not_a_mismatch()
        {
            var check = RecordCheck.Run(Ours(new(13, 5, 0, 0m, 0m)), Sleepers(new(11, 5, 0, 0m, 0m)))!;

            check.Compared.Should().Be(0);
            check.Skipped.Should().Be(1);
            check.Mismatches.Should().BeEmpty();
        }

        [Fact]
        public void Incomplete_records_are_not_checked_at_all() =>
            RecordCheck.Run(null, Sleepers(new(13, 5, 0, 0m, 0m))).Should().BeNull();

        [Fact]
        public void A_roster_Sleeper_sent_no_record_for_is_skipped() =>
            RecordCheck.Run(Ours(new(13, 5, 0, 0m, 0m)), Sleepers(null))!.Skipped.Should().Be(1);
    }
}
