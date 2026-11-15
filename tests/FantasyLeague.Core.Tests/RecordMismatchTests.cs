using FantasyLeague.Seasons;
using FantasyLeague.Sleeper;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// The third check: our head-to-head record against Sleeper's (T68).
    /// </summary>
    public class RecordMismatchTests
    {
        private static Dictionary<int, SleeperTeam> Sleepers(TeamRecord? record) => new() { [1] = new("One", "u1", record) };

        private static Dictionary<int, TeamRecord> Ours(TeamRecord record) => new() { [1] = record };

        [Fact]
        public void Agreeing_records_are_no_mismatch() =>
            RecordMismatch.Find(Ours(new(13, 5, 0, 1m, 2m)), Sleepers(new(13, 5, 0, 9m, 9m)))
                .Should().BeEmpty("points are not compared: a later stat correction moves only Sleeper's");

        [Fact]
        public void A_different_result_over_the_same_games_is_a_mismatch() =>
            RecordMismatch.Find(Ours(new(13, 5, 0, 0m, 0m)), Sleepers(new(12, 6, 0, 0m, 0m)))
                .Should().ContainSingle().Which.RosterId.Should().Be(1);

        [Fact]
        public void Sleeper_a_week_behind_is_a_lag_not_a_mismatch() =>
            RecordMismatch.Find(Ours(new(13, 5, 0, 0m, 0m)), Sleepers(new(11, 5, 0, 0m, 0m))).Should().BeEmpty();

        [Fact]
        public void Incomplete_records_are_not_checked() =>
            RecordMismatch.Find(null, Sleepers(new(13, 5, 0, 0m, 0m))).Should().BeEmpty();

        [Fact]
        public void A_roster_Sleeper_sent_no_record_for_is_not_checked() =>
            RecordMismatch.Find(Ours(new(13, 5, 0, 0m, 0m)), Sleepers(null)).Should().BeEmpty();
    }
}
