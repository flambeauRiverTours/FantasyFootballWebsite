using FantasyLeague.Seasons;
using FluentAssertions;
using Microsoft.Extensions.Primitives;

namespace FantasyLeague.Core.Tests
{
    public class ComputeETagTests
    {
        private static readonly IReadOnlyDictionary<string, decimal> NoAttribution = new Dictionary<string, decimal>();

        private static SeasonSnapshot Snapshot(
            int currentWeek = 2,
            int finalThroughWeek = 1,
            int season = 2026,
            params (int RosterId, (int Week, decimal Actual, decimal Ideal, bool IsFinal)[] Weeks)[] teams) =>
            new("Name", season, currentWeek, finalThroughWeek, Slot.Parse(["QB"]),
                [.. teams.Select(t => new TeamSeason(t.RosterId, $"Team {t.RosterId}",
                    [.. t.Weeks.Select(w => new TeamWeek(t.RosterId, w.Week, w.Actual, w.Ideal, [], [], [], w.IsFinal, NoAttribution))]))],
                [], [], new HashSet<int>());

        private static SeasonSnapshot OneTeam(decimal actual = 100m, decimal ideal = 120m, bool isFinal = true) =>
            Snapshot(teams: (1, [(1, actual, ideal, isFinal)]));

        [Fact]
        public void The_tag_is_weak() => ComputeETagOf(OneTeam()).Should().StartWith("W/\"");

        [Fact]
        public void The_tag_is_quoted() => ComputeETagOf(OneTeam()).Should().EndWith("\"");

        [Fact]
        public void The_same_data_gives_the_same_tag() => ComputeETagOf(OneTeam()).Should().Be(ComputeETagOf(OneTeam()));

        [Fact]
        public void A_changed_score_changes_the_tag()
        {
            ComputeETagOf(OneTeam(actual: 100m))
                .Should().NotBe(ComputeETagOf(OneTeam(actual: 100.5m)));
        }

        [Fact]
        public void A_changed_ideal_changes_the_tag()
        {
            ComputeETagOf(OneTeam(ideal: 120m))
                .Should().NotBe(ComputeETagOf(OneTeam(ideal: 121m)));
        }

        [Fact]
        public void A_week_finalising_changes_the_tag()
        {
            ComputeETagOf(OneTeam(isFinal: false))
                .Should().NotBe(ComputeETagOf(OneTeam(isFinal: true)));
        }

        [Fact]
        public void A_new_week_changes_the_tag() => ComputeETagOf(Snapshot(currentWeek: 2)).Should().NotBe(ComputeETagOf(Snapshot(currentWeek: 3)));

        [Fact]
        public void A_different_season_changes_the_tag() => ComputeETagOf(Snapshot(season: 2026)).Should().NotBe(ComputeETagOf(Snapshot(season: 2025)));

        [Fact]
        public void Team_order_does_not_change_the_tag()
        {
            var ascending = Snapshot(teams:
                [(1, [(1, 10m, 10m, true)]), (2, [(1, 20m, 20m, true)])]);
            var descending = Snapshot(teams:
                [(2, [(1, 20m, 20m, true)]), (1, [(1, 10m, 10m, true)])]);

            ComputeETagOf(ascending).Should().Be(ComputeETagOf(descending));
        }

        [Fact]
        public void One_teams_change_changes_the_whole_tag()
        {
            var before = Snapshot(teams:
                [(1, [(1, 10m, 10m, true)]), (2, [(1, 20m, 20m, true)])]);
            var after = Snapshot(teams:
                [(1, [(1, 10m, 10m, true)]), (2, [(1, 20.1m, 20m, true)])]);

            ComputeETagOf(before).Should().NotBe(ComputeETagOf(after));
        }

        [Fact]
        public void The_tag_names_the_season_and_week() => ComputeETagOf(Snapshot(season: 2026, currentWeek: 7)).Should().Contain("2026-wk7-");

        [Fact]
        public void The_time_of_the_response_does_not_change_the_tag()
        {
            // asOfUtc differs on every response; including it would make every poll a 200
            var snapshot = OneTeam();

            SeasonPayloadMapper.ComputeETag(SeasonPayloadMapper.ToSeasonPayload(snapshot, DateTimeOffset.UnixEpoch))
                .Should().Be(SeasonPayloadMapper.ComputeETag(SeasonPayloadMapper.ToSeasonPayload(snapshot, DateTimeOffset.UtcNow)));
        }

        [Fact]
        public void A_renamed_league_changes_the_tag()
        {
            ComputeETagOf(OneTeam() with { LeagueName = "Before" })
                .Should().NotBe(ComputeETagOf(OneTeam() with { LeagueName = "After" }));
        }

        [Fact]
        public void Excluding_a_roster_changes_the_tag()
        {
            var both = Snapshot(teams: [(1, [(1, 10m, 10m, true)]), (2, [(1, 20m, 20m, true)])]);

            ComputeETagOf(both)
                .Should().NotBe(ComputeETagOf(both with { ExcludedRosterIds = new HashSet<int> { 2 } }));
        }

        private static string ComputeETagOf(SeasonSnapshot snapshot) => ETag.Of(snapshot);
    }

    internal static class ETag
    {
        public static string Of(SeasonSnapshot snapshot) =>
            SeasonPayloadMapper.ComputeETag(SeasonPayloadMapper.ToSeasonPayload(snapshot, DateTimeOffset.UnixEpoch));
    }

    public class MatchesETagTests
    {
        private const string Tag = "W/\"2026-wk7-ABCDEF0123456789\"";
        private static readonly IReadOnlyDictionary<string, decimal> NoAttribution = new Dictionary<string, decimal>();

        [Fact]
        public void An_absent_header_never_matches() => SeasonPayloadMapper.MatchesETag(StringValues.Empty, Tag).Should().BeFalse();

        [Fact]
        public void The_same_tag_matches() => SeasonPayloadMapper.MatchesETag(new StringValues(Tag), Tag).Should().BeTrue();

        [Fact]
        public void The_tag_without_its_weak_prefix_still_matches()
        {
            SeasonPayloadMapper.MatchesETag(new StringValues("\"2026-wk7-ABCDEF0123456789\""), Tag)
                .Should().BeTrue("weak comparison ignores the prefix");
        }

        [Fact]
        public void A_tag_inside_a_list_matches()
        {
            SeasonPayloadMapper.MatchesETag(new StringValues($"\"something-else\", {Tag}"), Tag)
                .Should().BeTrue("If-None-Match is a comma separated list");
        }

        [Fact]
        public void A_tag_across_repeated_headers_matches()
        {
            SeasonPayloadMapper.MatchesETag(new StringValues(["\"other\"", Tag]), Tag)
                .Should().BeTrue();
        }

        [Fact]
        public void A_different_tag_does_not_match()
        {
            SeasonPayloadMapper.MatchesETag(new StringValues("W/\"2026-wk6-0000000000000000\""), Tag)
                .Should().BeFalse();
        }

        [Fact]
        public void A_stale_tag_from_the_previous_poll_does_not_match()
        {
            var before = ETag.Of(
                new SeasonSnapshot("Name", 2026, 1, 0, Slot.Parse(["QB"]),
                    [new TeamSeason(1, "One", [new TeamWeek(1, 1, 10m, 10m, [], [], [], false, NoAttribution)])],
                    [], [], new HashSet<int>()));

            var after = ETag.Of(
                new SeasonSnapshot("Name", 2026, 1, 0, Slot.Parse(["QB"]),
                    [new TeamSeason(1, "One", [new TeamWeek(1, 1, 17m, 20m, [], [], [], false, NoAttribution)])],
                    [], [], new HashSet<int>()));

            SeasonPayloadMapper.MatchesETag(new StringValues(before), after)
                .Should().BeFalse("a 304 here would freeze the page mid-Sunday");
        }

        [Fact]
        public void A_slot_attribution_change_alone_changes_the_tag()
        {
            // T48: the mid-season rules guard can flip a week's attribution to
            // empty with Actual and Ideal unchanged; the ETag has to notice anyway
            var before = ETag.Of(
                new SeasonSnapshot("Name", 2026, 1, 0, Slot.Parse(["QB"]),
                    [new TeamSeason(1, "One", [new TeamWeek(1, 1, 10m, 10m, [], [], [],
                        true, new Dictionary<string, decimal> { ["QB"] = 10m })])],
                    [], [], new HashSet<int>()));

            var after = ETag.Of(
                new SeasonSnapshot("Name", 2026, 1, 0, Slot.Parse(["QB"]),
                    [new TeamSeason(1, "One", [new TeamWeek(1, 1, 10m, 10m, [], [], [], true, NoAttribution)])],
                    [], [], new HashSet<int>()));

            SeasonPayloadMapper.MatchesETag(new StringValues(before), after).Should().BeFalse();
        }

        [Fact]
        public void Garbage_does_not_match_and_does_not_throw() => SeasonPayloadMapper.MatchesETag(new StringValues("not an etag"), Tag).Should().BeFalse();

        [Fact]
        public void A_wildcard_does_not_match() => SeasonPayloadMapper.MatchesETag(new StringValues("*"), Tag).Should().BeFalse();
    }
}
