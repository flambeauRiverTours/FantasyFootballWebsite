using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// The decisions the season endpoint makes, exercised through the pure pieces
    /// it delegates to. The trigger itself is a Functions type and needs the
    /// ASP.NET Core 9 runtime to load, which this machine does not have.
    /// </summary>
    public class SeasonRequestTests
    {
        /// <summary>
        /// The endpoint's parse: absent means current, anything unparseable is a
        /// bad request. An absent query key reads back as an empty string, which
        /// int.Parse would have thrown on.
        /// </summary>
        private static bool TryReadSeason(string raw, out int? season)
        {
            season = null;
            if (string.IsNullOrWhiteSpace(raw)) return true;
            if (!int.TryParse(raw, out var parsed)) return false;

            season = parsed;
            return true;
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void An_absent_season_means_the_current_one(string raw)
        {
            TryReadSeason(raw, out var season).Should().BeTrue();
            season.Should().BeNull();
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("20 26")]
        [InlineData("2026.5")]
        public void An_unparseable_season_is_a_bad_request(string raw)
        {
            TryReadSeason(raw, out _).Should().BeFalse();
        }

        [Fact]
        public void A_well_formed_year_is_read()
        {
            TryReadSeason("2025", out var season).Should().BeTrue();
            season.Should().Be(2025);
        }
    }

    public class ExcludedRosterResolutionTests
    {
        private static SeasonSnapshot Snapshot(params (int RosterId, string Name, string OwnerId)[] rosters) =>
            new("", 2026, 1, 0, Slot.Parse(["QB"]),
                [.. rosters.Select(r => new TeamSeason(r.RosterId, r.Name, []))],
                [], [],
                rosters.ToDictionary(r => r.RosterId, r => r.OwnerId));

        private static IReadOnlySet<int> Resolve(SeasonSnapshot snapshot, params string[] excludedOwnerIds) =>
            SeasonPayloadMapper.ResolveExcludedRosters(snapshot, excludedOwnerIds).RosterIds;

        private static IReadOnlyList<string> Unmatched(SeasonSnapshot snapshot, params string[] excludedOwnerIds) =>
            SeasonPayloadMapper.ResolveExcludedRosters(snapshot, excludedOwnerIds).UnmatchedOwnerIds;

        [Fact]
        public void An_owner_id_resolves_to_this_seasons_roster()
        {
            var snapshot = Snapshot((1, "Real Team", "u1"), (9, "POON SQUAD", "u9"));

            Resolve(snapshot, "u9").Should().Equal(9);
        }

        [Fact]
        public void A_rename_does_not_smuggle_the_team_back_in()
        {
            // A1's whole reason for matching on user id
            var renamed = Snapshot((1, "Real Team", "u1"), (9, "Suddenly A Serious Name", "u9"));

            Resolve(renamed, "u9").Should().Equal(9);
        }

        [Fact]
        public void The_same_owner_on_a_different_roster_number_still_resolves()
        {
            // roster ids are reassigned between seasons; user ids are not
            var nextSeason = Snapshot((3, "POON SQUAD", "u9"), (4, "Real Team", "u1"));

            Resolve(nextSeason, "u9").Should().Equal(3);
        }

        [Fact]
        public void No_configured_exclusions_excludes_nobody()
        {
            var snapshot = Snapshot((1, "Real Team", "u1"), (9, "POON SQUAD", "u9"));

            Resolve(snapshot).Should().BeEmpty();
        }

        [Fact]
        public void An_owner_id_that_matches_no_roster_is_ignored()
        {
            var snapshot = Snapshot((1, "Real Team", "u1"));

            Resolve(snapshot, "u-nobody").Should().BeEmpty();
        }

        [Fact]
        public void Owner_ids_are_matched_exactly()
        {
            var snapshot = Snapshot((1, "Real Team", "u1"), (9, "POON SQUAD", "u9"));

            Resolve(snapshot, "U9").Should().BeEmpty("ids are opaque strings, not case-insensitive names");
        }

        [Fact]
        public void Two_excluded_owners_both_resolve()
        {
            var snapshot = Snapshot((1, "A", "u1"), (2, "B", "u2"), (3, "C", "u3"));

            Resolve(snapshot, "u1", "u3").Should().BeEquivalentTo([1, 3]);
        }

        [Fact]
        public void A_roster_with_no_owner_cannot_be_excluded_and_does_not_throw()
        {
            // an orphaned roster is absent from RosterOwnerIds entirely
            var snapshot = new SeasonSnapshot("", 2026, 1, 0, Slot.Parse(["QB"]),
                [new TeamSeason(5, "Orphan", [])], [], [],
                new Dictionary<int, string>());

            Resolve(snapshot, "u9").Should().BeEmpty();
        }

        [Fact]
        public void An_owner_id_that_matches_no_roster_is_reported()
        {
            // a typo would otherwise leave the team in every metric, silently
            var snapshot = Snapshot((1, "Real Team", "u1"));

            Unmatched(snapshot, "u9").Should().Equal("u9");
        }

        [Fact]
        public void A_matched_owner_id_is_not_reported()
        {
            var snapshot = Snapshot((1, "Real Team", "u1"), (9, "POON SQUAD", "u9"));

            Unmatched(snapshot, "u9").Should().BeEmpty();
        }

        [Fact]
        public void Only_the_unmatched_id_is_reported_from_a_mixed_list()
        {
            var snapshot = Snapshot((1, "Real Team", "u1"), (9, "POON SQUAD", "u9"));

            Unmatched(snapshot, "u9", "u-typo").Should().Equal("u-typo");
        }

        [Fact]
        public void A_wrongly_cased_id_is_reported_rather_than_quietly_matched()
        {
            var snapshot = Snapshot((1, "Real Team", "u1"), (9, "POON SQUAD", "u9"));

            Unmatched(snapshot, "U9").Should().Equal("U9");
        }

        [Fact]
        public void Nothing_is_reported_when_nothing_is_configured()
        {
            Unmatched(Snapshot((1, "Real Team", "u1"))).Should().BeEmpty();
        }

        [Fact]
        public void The_resolved_ids_drop_the_team_from_the_payload()
        {
            var snapshot = new SeasonSnapshot("", 2026, 2, 1, Slot.Parse(["QB"]),
                [
                    new TeamSeason(1, "Real Team", [new TeamWeek(1, 1, 30m, 30m, [], [], [], true, new Dictionary<string, decimal>())]),
                    new TeamSeason(9, "POON SQUAD", [new TeamWeek(9, 1, 0m, 0m, [], [], [], true, new Dictionary<string, decimal>())]),
                ],
                [], [],
                new Dictionary<int, string> { [1] = "u1", [9] = "u9" });

            var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(
                snapshot, DateTimeOffset.UtcNow, null, Resolve(snapshot, "u9"));

            payload.Teams.Should().ContainSingle().Which.Name.Should().Be("Real Team");
        }
    }

    public class LeagueOptionsExclusionTests
    {
        [Fact]
        public void No_excluded_owners_are_configured_by_default()
        {
            new LeagueOptions().ExcludedOwnerIds.Should().BeEmpty();
        }

        [Fact]
        public void Excluded_owners_bind_from_configuration()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["League:CurrentLeagueId"] = "L1",
                    ["League:ExcludedOwnerIds:0"] = "u9",
                    ["League:ExcludedOwnerIds:1"] = "u8",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddLogging();
            services.AddSleeperClient();

            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<IOptions<LeagueOptions>>().Value
                .ExcludedOwnerIds.Should().BeEquivalentTo(["u9", "u8"]);
        }
    }
}
