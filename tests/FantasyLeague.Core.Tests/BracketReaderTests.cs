using System.Net;
using FantasyLeague.Sleeper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Sleeper's <c>winners_bracket</c>, read tolerantly (T69, A46).
    /// </summary>
    public class BracketReaderTests
    {
        private static async Task<IReadOnlyList<PlayoffMatch>> Read(string body)
        {
            var services = new ServiceCollection();
            services.AddSleeperClient(o => o.Retry.Delay = TimeSpan.Zero)
                    .ConfigurePrimaryHttpMessageHandler(() => FakeSleeper.Answering(HttpStatusCode.OK, body));
            using var provider = services.BuildServiceProvider();
            return await provider.GetRequiredService<ISleeperReader>().GetWinnersBracketAsync("L1", TimeSpan.FromMinutes(5), default);
        }

        [Fact]
        public async Task A_match_maps_its_sides_winner_and_placement()
        {
            var bracket = await Read("""[{"r":2,"m":3,"t1":1,"t2":4,"w":4,"l":1,"t2_from":{"w":1},"p":1}]""");

            bracket.Should().ContainSingle().Which.Should().Be(new PlayoffMatch(
                2, 3, new BracketSide(1, BracketSource.None, 0), new BracketSide(4, BracketSource.WinnerOf, 1), 4, 1));
        }

        [Fact]
        public async Task A_side_still_to_come_names_where_it_comes_from()
        {
            var match = (await Read("""[{"r":2,"m":5,"t1":null,"t2":null,"w":null,"l":null,"t1_from":{"l":1},"t2_from":{"l":2},"p":5}]"""))[0];

            match.First.Should().Be(new BracketSide(null, BracketSource.LoserOf, 1));
            match.Second.Should().Be(new BracketSide(null, BracketSource.LoserOf, 2));
            match.Winner.Should().BeNull();
        }

        [Fact]
        public async Task A_side_given_as_a_reference_in_place_of_a_roster_reads_as_one() =>
            // Sleeper's docs describe t1 as "a roster_id, or a reference" such as {"w":1}
            (await Read("""[{"r":2,"m":3,"t1":{"w":1},"t2":2}]"""))[0].First
                .Should().Be(new BracketSide(null, BracketSource.WinnerOf, 1));

        [Fact]
        public async Task A_side_not_yet_seeded_comes_from_nowhere() =>
            (await Read("""[{"r":1,"m":1,"t1":null,"t2":6}]"""))[0].First.Should().Be(new BracketSide(null, BracketSource.None, 0));

        [Fact]
        public async Task A_match_deciding_no_place_has_no_placement() =>
            (await Read("""[{"r":1,"m":1,"t1":3,"t2":6}]"""))[0].Placement.Should().BeNull();

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        public async Task No_bracket_reads_as_an_empty_one(string body) =>
            (await Read(body)).Should().BeEmpty();

        [Fact]
        public async Task A_side_that_cannot_be_read_fails_the_bracket() =>
            await FluentActions.Invoking(() => Read("""[{"r":1,"m":1,"t1":"three","t2":6}]"""))
                .Should().ThrowAsync<InvalidDataException>();
    }
}
