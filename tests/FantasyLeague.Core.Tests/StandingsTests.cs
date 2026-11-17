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
        public void The_points_champion_is_the_points_leader() =>
            Standings.PointsLeaders([("A", 90m), ("B", 120m), ("C", 100m)], t => t.Item2).Select(t => t.Item1)
                .Should().Equal("B");

        [Fact]
        public void Tied_points_crown_everyone_tied() =>
            Standings.PointsLeaders([("A", 120m), ("B", 90m), ("C", 120m)], t => t.Item2).Select(t => t.Item1)
                .Should().Equal("A", "C");

        [Fact]
        public void A_season_nobody_scored_in_crowns_nobody() =>
            // a league rolled over and read before a game: complete by the calendar, empty in fact
            Standings.PointsLeaders([("A", 0m), ("B", 0m)], t => t.Item2).Should().BeEmpty();

        [Fact]
        public void The_record_champion_leads_on_win_rate_then_points_for() =>
            Standings.RecordLeaders([Named("Many", 9, 3, 0, 900m), Named("Best", 10, 2, 0, 800m), Named("Same", 10, 2, 0, 700m)], t => t.Record)
                .Select(t => t.Name).Should().Equal("Best");

        [Fact]
        public void A_tie_on_win_rate_and_points_for_crowns_both_and_the_name_decides_nothing() =>
            Standings.RecordLeaders([Named("Zulu", 10, 2, 0, 800m), Named("Alpha", 10, 2, 0, 800m), Named("Low", 1, 11, 0, 800m)], t => t.Record)
                .Select(t => t.Name).Should().BeEquivalentTo("Zulu", "Alpha");

        [Fact]
        public void Records_with_no_points_crown_nobody() =>
            Standings.RecordLeaders([Named("A", 0, 0, 0), Named("B", 0, 0, 0)], t => t.Record).Should().BeEmpty();

        [Fact]
        public void A_tie_leaves_half_a_game()
        {
            TeamRecord[] records = [new(3, 1, 0, 0m, 0m), new(1, 2, 1, 0m, 0m)];

            Standings.GamesBack(records[1], records).Should().Be(1.5m);
        }
    }
}
