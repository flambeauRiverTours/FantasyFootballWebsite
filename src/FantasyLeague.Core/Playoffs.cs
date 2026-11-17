namespace FantasyLeague.Core
{
    /// <summary>
    /// The bracket's rounds in weeks, and when a match is settled (T69, A46).
    /// </summary>
    public static class Playoffs
    {
        /// <summary>
        /// Gets the weeks one round is played in.
        /// </summary>
        /// <param name="start">The league's first playoff week.</param>
        /// <param name="roundType">Sleeper's <c>playoff_round_type</c>: 0 a week a round, 1 a two-week final, 2 two weeks a round. An assumption (A46).</param>
        /// <param name="round">1 for the first round.</param>
        /// <param name="rounds">How many rounds the bracket has, for a two-week final.</param>
        /// <returns>The weeks, or null for a round type not known.</returns>
        public static IReadOnlyList<int>? RoundWeeks(int start, int roundType, int round, int rounds) => roundType switch
        {
            0 => [start + round - 1],
            1 => round == rounds ? [start + round - 1, start + round] : [start + round - 1],
            2 => [start + (2 * (round - 1)), start + (2 * (round - 1)) + 1],
            _ => null,
        };

        /// <summary>
        /// Gets every round's weeks.
        /// </summary>
        /// <param name="bracket">Sleeper's matches.</param>
        /// <param name="start">The league's first playoff week.</param>
        /// <param name="roundType">Sleeper's <c>playoff_round_type</c>.</param>
        /// <returns>The weeks by round; null for an empty bracket, a round type not known, or a round past week 18, since a partial bracket misleads.</returns>
        public static IReadOnlyDictionary<int, IReadOnlyList<int>>? Weeks(IReadOnlyList<PlayoffMatch> bracket, int start, int roundType)
        {
            if (bracket.Count == 0)
            {
                return null;
            }

            var rounds = bracket.Max(m => m.Round);
            var weeks = new Dictionary<int, IReadOnlyList<int>>();
            for (var round = 1; round <= rounds; round++)
            {
                if (RoundWeeks(start, roundType, round, rounds) is not { } played || played.Any(w => w > RegularSeason.NFLWeeks))
                {
                    return null;
                }

                weeks[round] = played;
            }

            return weeks;
        }

        /// <summary>
        /// Decides whether a match's result stands.
        /// </summary>
        /// <param name="match">The match.</param>
        /// <param name="weeks">Its round's weeks.</param>
        /// <param name="finalThroughWeek">The last settled week.</param>
        /// <returns>
        /// True once Sleeper named a winner and every week is final; for a round ending in week 18,
        /// once Sleeper named a winner, since its state after week 18 is unobserved (A37, A46).
        /// </returns>
        public static bool IsSettled(PlayoffMatch match, IReadOnlyList<int> weeks, int finalThroughWeek) =>
            match.Winner is not null && (weeks[^1] == RegularSeason.NFLWeeks || weeks.All(w => w <= finalThroughWeek));
    }
}
