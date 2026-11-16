using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// The head-to-head ranking rule: the cases of the page's <c>rankByRecord</c> tests, so the two cannot drift (T72a).
    /// </summary>
    public class StandingsTests
    {
        private static (string Name, TeamRecord Record) Named(string name, int wins, int losses, int ties, decimal pointsFor = 0m) =>
            (name, new TeamRecord(wins, losses, ties, pointsFor, 0m));

        private static IEnumerable<string> Ranked(params (string Name, TeamRecord Record)[] teams) =>
            Standings.RankByRecord(teams, t => t.Record, t => t.Name).Select(t => t.Name);

        [Fact]
        public void A_tie_counts_as_half_a_win() =>
            // 2-1-1 is 2.5 of 4; 2-2 is 2 of 4
            Ranked(Named("Even", 2, 2, 0), Named("Tied", 2, 1, 1)).Should().Equal("Tied", "Even");

        [Fact]
        public void An_equal_win_rate_breaks_on_points_for_then_on_name() =>
            Ranked(Named("Zulu", 2, 2, 0, 300m), Named("Low", 1, 1, 0, 100m), Named("Alpha", 2, 2, 0, 300m))
                .Should().Equal("Alpha", "Zulu", "Low");

        [Fact]
        public void At_0_0_names_decide_compared_as_the_page_compares_them_not_by_code_point() =>
            // before any week settles the name is the whole order; ordinal order would put "Bravo" first
            Ranked(Named("Bravo", 0, 0, 0), Named("alpha", 0, 0, 0)).Should().Equal("alpha", "Bravo");

        [Fact]
        public void Games_back_counts_from_the_best_wins_less_losses_not_the_top_row()
        {
            // a bye leaves different game counts: 4-0 ranks first, but 7-1 has the best margin
            TeamRecord[] records = [new(4, 0, 0, 0m, 0m), new(5, 1, 0, 0m, 0m), new(7, 1, 0, 0m, 0m), new(1, 3, 0, 0m, 0m)];

            records.Select(r => Standings.GamesBack(r, records)).Should().Equal(1m, 1m, 0m, 4m);
        }

        [Fact]
        public void A_tie_leaves_half_a_game()
        {
            TeamRecord[] records = [new(3, 1, 0, 0m, 0m), new(1, 2, 1, 0m, 0m)];

            Standings.GamesBack(records[1], records).Should().Be(1.5m);
        }
    }
}
