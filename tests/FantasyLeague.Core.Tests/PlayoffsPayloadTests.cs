using System.Text.Json;
using FantasyLeague.Seasons;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// The bracket on the page, the season's end and its champion (T69, A46).
    /// </summary>
    public class PlayoffsPayloadTests
    {
        private static readonly DateTimeOffset AsOf = new(2027, 1, 2, 12, 0, 0, TimeSpan.Zero);

        private static readonly IReadOnlyDictionary<string, decimal> NoAttribution = new Dictionary<string, decimal>();

        private static BracketSide Team(int rosterId) => new(rosterId, BracketSource.None, 0);

        private static BracketSide Winner(int match, int? rosterId = null) => new(rosterId, BracketSource.WinnerOf, match);

        private static BracketSide Loser(int match, int? rosterId = null) => new(rosterId, BracketSource.LoserOf, match);

        // Sleeper's six-team bracket: rosters 1 and 2 have byes; round 2 holds the 5th-place match
        private static IReadOnlyList<PlayoffMatch> SixTeams(bool final = false) =>
        [
            new(1, 1, Team(3), Team(6), 3, null),
            new(1, 2, Team(4), Team(5), 4, null),
            new(2, 3, Team(1), Winner(1, 3), 1, null),
            new(2, 4, Team(2), Winner(2, 4), 2, null),
            new(2, 5, Loser(1, 6), Loser(2, 5), 5, 5),
            new(3, 7, Loser(3, 3), Loser(4, 4), final ? 3 : null, 3),
            new(3, 6, Winner(3, 1), Winner(4, 2), final ? 2 : null, 1),
        ];

        // six rosters through week 17; points are the roster id plus the week, so sums are checkable
        private static TeamSeason Roster(int rosterId, int weeks, int finalThrough) =>
            new(rosterId, $"Team {rosterId}",
                [.. Enumerable.Range(1, weeks).Select(w => new TeamWeek(
                    rosterId, w, rosterId + w, rosterId + w, [], [], [], w <= finalThrough, NoAttribution, w <= 14 ? ((rosterId + 1) / 2) : 0))]);

        private static SeasonSnapshot Season(
            int currentWeek,
            IReadOnlyList<PlayoffMatch>? bracket,
            int playoffWeekStart = 15,
            int roundType = 0,
            LeagueFormat format = LeagueFormat.HeadToHead,
            bool isCurrent = true)
        {
            var finalThrough = WeekFinality.FinalThroughWeek(currentWeek);
            var weeks = Math.Min(currentWeek, RegularSeason.NFLWeeks);
            return new SeasonSnapshot("", 2026, currentWeek, finalThrough, Slot.Parse(["QB", "BN"]),
                [.. Enumerable.Range(1, 6).Select(id => Roster(id, weeks, finalThrough))], [], [], isCurrent,
                RegularSeason.LastWeek(playoffWeekStart), format, [], false, null, bracket, roundType);
        }

        private static SeasonPayload Map(SeasonSnapshot snapshot) => SeasonPayloadMapper.ToSeasonPayload(snapshot, AsOf);

        [Fact]
        public void The_rounds_are_named_from_the_final_back()
        {
            var playoffs = Map(Season(16, SixTeams())).Playoffs!;

            playoffs.Rounds.Select(r => r.Name).Should().Equal("Round 1", "Semifinals", "Final");
            playoffs.Rounds.Select(r => r.Weeks).Should().BeEquivalentTo(new[] { new[] { 15 }, new[] { 16 }, new[] { 17 } });
        }

        [Fact]
        public void A_round_s_main_matches_come_before_its_placement_matches()
        {
            var playoffs = Map(Season(18, SixTeams())).Playoffs!;

            playoffs.Rounds[1].Matches.Select(m => (m.Match, m.Placement)).Should().Equal((3, null), (4, null), (5, 5));
            playoffs.Rounds[2].Matches.Select(m => (m.Match, m.Placement)).Should().Equal((6, 1), (7, 3));
        }

        [Fact]
        public void A_side_to_come_names_its_source_and_has_no_points()
        {
            var bracket = SixTeams().Select(m => m.Match == 6 ? m with { First = Winner(3), Second = Winner(4) } : m).ToList();

            var side = Map(Season(16, bracket)).Playoffs!.Rounds[2].Matches.Single(m => m.Match == 6).Sides[0];

            side.Should().Be(new BracketSidePayload(null, null, new BracketFromPayload(3, null), null, false));
        }

        [Fact]
        public void A_side_s_points_are_summed_over_its_round_s_weeks()
        {
            // type 2 from week 15: round 1 is weeks 15 and 16, and roster 3 scores 18 then 19
            var bracket = SixTeams().Where(m => m.Round == 1).Select(m => m with { Placement = m.Match == 1 ? 1 : null }).ToList();

            var side = Map(Season(17, bracket, roundType: 2)).Playoffs!.Rounds[0].Matches.Single(m => m.Match == 1).Sides[0];

            side.Points.Should().Be(37m);
            side.Name.Should().Be("Team 3");
        }

        [Fact]
        public void A_result_waits_for_its_weeks_and_for_Sleeper()
        {
            var live = Map(Season(15, SixTeams())).Playoffs!.Rounds[0].Matches[0];
            live.IsSettled.Should().BeFalse("week 15 is still live, though Sleeper's w is set");
            live.Sides.Should().OnlyContain(s => !s.Won);

            var settled = Map(Season(16, SixTeams())).Playoffs!.Rounds[0].Matches[0];
            settled.IsSettled.Should().BeTrue();
            settled.Sides.Select(s => s.Won).Should().Equal(true, false);

            var unnamed = SixTeams().Select(m => m.Match == 1 ? m with { Winner = null } : m).ToList();
            Map(Season(16, unnamed)).Playoffs!.Rounds[0].Matches[0].IsSettled.Should().BeFalse("Sleeper has not named the winner");
        }

        [Fact]
        public void The_season_of_a_playoff_league_ends_at_its_final()
        {
            var regularDone = Map(Season(16, SixTeams()));
            regularDone.SeasonEndWeek.Should().Be(17);
            regularDone.IsComplete.Should().BeFalse("the bracket is still being played");
            regularDone.Champions.Should().BeNull("a playoff league never crowns its table leader");

            var done = Map(Season(18, SixTeams(final: true)));
            done.IsComplete.Should().BeTrue();
            done.Champions.Should().Equal(2);
            done.Playoffs!.Rounds[2].Matches[0].Sides.Select(s => s.Won).Should().Equal(false, true);
        }

        [Fact]
        public void A_final_in_week_18_is_crowned_once_Sleeper_names_its_winner()
        {
            // start 17, a week a round: the final is week 18, which the state may never pass (A37)
            var bracket = new List<PlayoffMatch>
            {
                new(1, 1, Team(1), Team(4), 1, null),
                new(1, 2, Team(2), Team(3), 2, null),
                new(2, 3, Winner(1, 1), Winner(2, 2), 2, 1),
            };

            var payload = Map(Season(18, bracket, playoffWeekStart: 17));

            payload.Champions.Should().Equal(2);
            payload.IsComplete.Should().BeTrue();
        }

        [Fact]
        public void An_excluded_roster_that_wins_the_final_is_crowned()
        {
            // Q9: the placeholder has no table row, so its name comes from the bracket
            var snapshot = Season(18, SixTeams(final: true));
            var placeholder = snapshot.Teams.Single(t => t.RosterId == 2) with { Name = "Placeholder" };
            snapshot = snapshot with { Teams = [.. snapshot.Teams.Where(t => t.RosterId != 2)], ExcludedTeams = [placeholder] };

            var payload = Map(snapshot);

            payload.Champions.Should().Equal(2);
            payload.Teams.Should().NotContain(t => t.RosterId == 2);
            payload.Playoffs!.Rounds[2].Matches[0].Sides[1].Name.Should().Be("Placeholder");
        }

        [Fact]
        public void Without_a_bracket_a_playoff_league_s_season_runs_to_week_18_and_crowns_nobody()
        {
            // the read failed in week 15: one failure must not mark every open page complete
            var payload = Map(Season(15, null));

            payload.Playoffs.Should().BeNull();
            payload.SeasonEndWeek.Should().Be(RegularSeason.NFLWeeks);
            payload.IsComplete.Should().BeFalse();
            payload.Champions.Should().BeNull();
        }

        [Fact]
        public void A_total_points_league_has_no_bracket_and_ends_at_its_regular_season()
        {
            var payload = Map(Season(16, SixTeams(), format: LeagueFormat.TotalPoints));

            payload.Playoffs.Should().BeNull();
            payload.SeasonEndWeek.Should().Be(payload.LastRegularWeek);
            payload.IsComplete.Should().BeTrue();
            JsonSerializer.Serialize(payload, JsonSerializerOptions.Web).Should().NotContain("playoffs");
        }

        [Fact]
        public void A_past_season_s_final_is_settled_once_Sleeper_named_a_winner_even_if_its_week_was_never_stored()
        {
            // stored through week 15; the final (week 17) was never stored, but the season is over
            var payload = Map(Season(16, SixTeams(final: true), isCurrent: false));

            var final = payload.Playoffs!.Rounds[2].Matches[0];
            final.IsSettled.Should().BeTrue();
            final.Sides.Select(s => s.Won).Should().Equal(false, true);
            payload.Champions.Should().Equal(2);
        }

        [Fact]
        public void A_past_season_is_complete_even_without_its_bracket() =>
            Map(Season(17, null, isCurrent: false)).IsComplete.Should().BeTrue();
    }
}
