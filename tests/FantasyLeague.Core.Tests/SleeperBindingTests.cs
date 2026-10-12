using System.Text.Json;
using FantasyLeague.Sleeper;
using FluentAssertions;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// The DTOs against payloads Sleeper actually returned, rather than against
    /// the shapes the design assumed it would.
    /// </summary>
    public class SleeperBindingTests
    {
        private static string FixtureText(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

        private static T Read<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, SleeperJson.Options)!;

        [Fact]
        public void The_captured_week_binds_to_matchup_dtos()
        {
            var matchups = Read<List<MatchupDto>>(FixtureText("wk1-matchups.json"));

            matchups.Should().HaveCount(6);
            // Not a fixed count: in week 1 one roster carries 20 players to
            // everyone else's 19. What always holds is that every starter is
            // on the roster, bar the "0" that marks an empty slot.
            matchups.Should().OnlyContain(m =>
                m.Starters!.Where(id => id != "0").All(id => m.Players!.Contains(id)));
            matchups.Should().OnlyContain(m => m.Starters!.Count == 11);
            matchups.Should().OnlyContain(m => m.StartersPoints!.Count == 11);
            matchups.Should().OnlyContain(m => m.PlayersPoints!.Count > 0);
            matchups.Should().OnlyContain(m => m.CustomPoints == null);
        }

        [Fact]
        public void Points_bind_as_decimal_without_drifting()
        {
            // Roster 2's eleven starter scores sum to 122.82000000000001 as
            // doubles. The invariant sum(starters_points) == points is stated
            // to the cent, so the values have to survive the parse exactly.
            var matchups = Read<List<MatchupDto>>(FixtureText("wk1-matchups.json"));
            var roster2 = matchups.Single(m => m.RosterId == 2);

            roster2.Points.Should().Be(122.82m);
            roster2.StartersPoints!.Sum().Should().Be(roster2.Points);
        }

        [Fact]
        public void Empty_starter_slots_survive_as_the_string_zero()
        {
            // Three of them across two rosters in this one week, so this is
            // routine rather than theoretical. (Still three in the full week-1
            // capture: one on roster 4, two on the placeholder roster.)
            var matchups = Read<List<MatchupDto>>(FixtureText("wk1-matchups.json"));

            matchups.SelectMany(m => m.Starters!).Count(id => id == "0").Should().Be(3);
        }

        [Fact]
        public void A_defence_id_is_a_team_abbreviation_not_a_number()
        {
            var matchups = Read<List<MatchupDto>>(FixtureText("wk1-matchups.json"));
            var ids = matchups.SelectMany(m => m.Players!).ToList();

            ids.Should().Contain(id => !id.All(char.IsDigit),
                "defences are identified as team abbreviations such as BAL");
        }

        [Fact]
        public void Season_binds_from_a_string()
        {
            // Sleeper sends "2026", not 2026. Without
            // JsonNumberHandling.AllowReadingFromString this throws.
            var state = Read<NflStateDto>(
                """{"season":"2026","season_type":"regular","week":1,"display_week":1,"previous_season":"2025"}""");

            state.Season.Should().Be(2026);
            state.Week.Should().Be(1);
            state.PreviousSeason.Should().Be(2025);
        }

        [Fact]
        public void A_first_season_league_binds_with_a_null_previous_league()
        {
            var league = Read<LeagueDto>(
                """
                {"league_id":"123","previous_league_id":null,"season":"2026","name":"Test",
                 "status":"in_season","total_rosters":6,
                 "roster_positions":["QB","QB","RB","RB","WR","WR","TE","FLEX","SUPER_FLEX","K","DEF","BN"]}
                """);

            league.PreviousLeagueId.Should().BeNull("a first season has nothing to roll over from");
            league.Season.Should().Be(2026);
            Slot.Parse(league.RosterPositions!).Should().HaveCount(11);
        }

        [Fact]
        public void Nulls_that_are_not_booleans_or_lists_still_bind()
        {
            // is_bot came back null on a real user, and co_owners null on every
            // roster. Neither is false or empty.
            var user = Read<LeagueUserDto>(
                """{"user_id":"1","display_name":"someone","is_bot":null,"metadata":null}""");
            var roster = Read<RosterDto>(
                """{"roster_id":3,"owner_id":"1","co_owners":null}""");

            user.IsBot.Should().BeNull();
            user.Metadata.Should().BeNull();
            roster.CoOwners.Should().BeNull();
        }
    }

    /// <summary>
    /// The mapping boundary: the decisions taken between wire and domain.
    /// </summary>
    public class SleeperMapperTests
    {
        private static LeagueUserDto User(string? displayName, string? teamName, bool metadataPresent = true) =>
            new("u1", displayName, IsBot: false,
                metadataPresent ? new LeagueUserMetadataDto(teamName) : null);

        [Fact]
        public void A_team_name_is_used_when_the_manager_set_one()
        {
            SleeperMapper.ResolveTeamName(User("jtreads", "START TOMMY CUTLETS"), 1)
                .Should().Be("START TOMMY CUTLETS");
        }

        [Theory]
        [InlineData(null)]      // key absent from metadata, which is what this league returns
        [InlineData("")]        // what the design predicted instead
        [InlineData("   ")]     // and the case neither considered
        public void An_unset_team_name_falls_back_to_the_display_name(string? teamName)
        {
            SleeperMapper.ResolveTeamName(User("kuzma4prez", teamName), 1)
                .Should().Be("kuzma4prez");
        }

        [Fact]
        public void A_missing_metadata_object_falls_back_too()
        {
            SleeperMapper.ResolveTeamName(User("sullynator9", null, metadataPresent: false), 1)
                .Should().Be("sullynator9");
        }

        [Fact]
        public void A_manager_with_no_name_at_all_still_gets_a_label() =>
            // Never render an empty string into the standings table.
            SleeperMapper.ResolveTeamName(User(null, null), 4).Should().Be("Roster 4");

        [Fact]
        public void A_commissioner_override_beats_the_scored_points()
        {
            // custom_points is what Sleeper displays once a commissioner has
            // corrected a score. Ignoring it desyncs the standings from the app.
            var matchup = new MatchupDto(
                RosterId: 2, MatchupId: 1, Points: 88.4m, CustomPoints: 91m,
                Players: [], Starters: [], StartersPoints: [], PlayersPoints: new Dictionary<string, decimal>());

            SleeperMapper.ToRosterWeek(matchup, new Dictionary<string, PlayerRef>())
                .Actual.Should().Be(91m);
        }

        [Fact]
        public void An_empty_starter_slot_keeps_its_place_as_null()
        {
            // Sleeper sends the string "0"; the slot averages must still line up
            var matchup = new MatchupDto(
                RosterId: 2, MatchupId: 1, Points: 10m, CustomPoints: null,
                Players: ["qb1"], Starters: ["qb1", "0", "wr1"], StartersPoints: [10m, 0m, 0m],
                PlayersPoints: new Dictionary<string, decimal>());

            SleeperMapper.ToSlotStarters(matchup).Should().Equal("qb1", null, "wr1");
        }

        [Fact]
        public void Without_an_override_the_scored_points_stand()
        {
            var matchup = new MatchupDto(
                RosterId: 2, MatchupId: 1, Points: 88.4m, CustomPoints: null,
                Players: [], Starters: [], StartersPoints: [], PlayersPoints: new Dictionary<string, decimal>());

            SleeperMapper.ToRosterWeek(matchup, new Dictionary<string, PlayerRef>())
                .Actual.Should().Be(88.4m);
        }

        [Fact]
        public void A_player_with_no_points_entry_scored_zero()
        {
            var map = new Dictionary<string, PlayerRef>
            {
                ["1"] = new("Played", "KC", ["QB"]),
                ["2"] = new("On a bye", "BAL", ["RB"]),
            };
            var matchup = new MatchupDto(
                RosterId: 1, MatchupId: 1, Points: 20m, CustomPoints: null,
                Players: ["1", "2"], Starters: ["1"], StartersPoints: [20m],
                PlayersPoints: new Dictionary<string, decimal> { ["1"] = 20m });

            var week = SleeperMapper.ToRosterWeek(matchup, map);

            week.Players.Should().HaveCount(2);
            week.Players.Single(p => p.Id.Value == "2").Points.Should().Be(0m);
        }

        [Fact]
        public void An_unresolved_player_is_reported_rather_than_dropped()
        {
            // The player map refreshes daily, so someone signed and rostered
            // between refreshes is genuinely absent for a few hours. Dropping him
            // silently lowers that team's ideal, and ideal >= actual cannot catch
            // an undercount.
            var map = new Dictionary<string, PlayerRef> { ["1"] = new("Known", "KC", ["QB"]) };
            var matchup = new MatchupDto(
                RosterId: 1, MatchupId: 1, Points: 10m, CustomPoints: null,
                Players: ["1", "99999"], Starters: ["1"], StartersPoints: [10m],
                PlayersPoints: new Dictionary<string, decimal> { ["1"] = 10m });

            var week = SleeperMapper.ToRosterWeek(matchup, map);

            week.Players.Should().ContainSingle();
            week.UnresolvedPlayerIds.Should().Equal("99999");
        }

        [Fact]
        public void Empty_slots_are_stripped_from_both_arrays()
        {
            var map = new Dictionary<string, PlayerRef> { ["1"] = new("Known", "KC", ["QB"]) };
            var matchup = new MatchupDto(
                RosterId: 1, MatchupId: 1, Points: 10m, CustomPoints: null,
                Players: ["1", "0"], Starters: ["1", "0"], StartersPoints: [10m, 0m],
                PlayersPoints: new Dictionary<string, decimal> { ["1"] = 10m });

            var week = SleeperMapper.ToRosterWeek(matchup, map);

            week.Players.Should().ContainSingle();
            week.Starters.Should().ContainSingle();
            week.UnresolvedPlayerIds.Should().BeEmpty("\"0\" is an empty slot, not a missing player");
        }

        [Fact]
        public void A_player_with_no_fantasy_positions_is_dropped_from_the_map()
        {
            // Around 330 of the dump's 12,200 entries have none, and nothing can
            // be started at no position.
            var dump = new Dictionary<string, SleeperPlayerDto>
            {
                ["1"] = new("1", "Startable", "KC", "QB", ["QB"]),
                ["2"] = new("2", "Long snapper", "KC", "LS", []),
                ["3"] = new("3", null, "KC", null, null),
                ["BAL"] = new("BAL", null, "BAL", "DEF", ["DEF"]),
            };

            var map = SleeperMapper.ToPlayerMap(dump);

            map.Keys.Should().BeEquivalentTo(["1", "BAL"]);
            map["BAL"].Name.Should().Be("BAL", "a defence has no full name, so the id stands in");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void An_unnamed_league_gets_the_default_name(string? name)
        {
            var league = new LeagueDto("L1", null, 2026, name, null, 6, ["QB"]);

            SleeperMapper.ToLeagueName(league).Should().Be("Fantasy Football League");
        }

        [Fact]
        public void A_named_league_keeps_its_name()
        {
            var league = new LeagueDto("L1", null, 2026, "Bosty Base", null, 6, ["QB"]);

            SleeperMapper.ToLeagueName(league).Should().Be("Bosty Base");
        }

        [Fact]
        public void A_league_with_no_roster_positions_is_rejected()
        {
            var league = new LeagueDto("L1", null, 2026, "Bosty Base", null, 6, null);

            var act = () => SleeperMapper.ToSlots(league);

            act.Should().Throw<InvalidDataException>();
        }

        [Fact]
        public void Unowned_rosters_are_left_out_of_the_owner_map()
        {
            var owners = SleeperMapper.ToRosterOwnerIds(
            [
                new(RosterId: 1, OwnerId: "u1", CoOwners: null),
                new(RosterId: 2, OwnerId: null, CoOwners: null),
            ]);

            owners.Should().BeEquivalentTo(new Dictionary<int, string> { [1] = "u1" });
        }

        [Fact]
        public void Roster_names_join_through_the_owner_id()
        {
            var users = new List<LeagueUserDto>
            {
                User("josiahlow", "Aaron Donald Relief Fund") with { UserId = "u1" },
                User("kuzma4prez", null) with { UserId = "u2" },
            };
            var rosters = new List<RosterDto>
            {
                new(RosterId: 1, OwnerId: "u1", CoOwners: null),
                new(RosterId: 2, OwnerId: "u2", CoOwners: null),
                new(RosterId: 3, OwnerId: null, CoOwners: null),   // orphaned roster
            };

            var names = SleeperMapper.ToTeamNames(users, rosters);

            names[1].Should().Be("Aaron Donald Relief Fund");
            names[2].Should().Be("kuzma4prez");
            names[3].Should().Be("Roster 3");
        }
    }
}
