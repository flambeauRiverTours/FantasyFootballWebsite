namespace FantasyLeague.Core
{
    /// <summary>
    /// Provides a solver for the ideal lineup problem in fantasy sports. The solver uses dynamic programming to calculate the maximum points that can be achieved by selecting players for the given slots, while respecting the position constraints of each slot.
    /// </summary>
    public static class IdealLineupSolver
    {
        /// <summary>
        /// The most players a single call can consider. The chosen set is carried
        /// as a <see cref="long"/> bitmask, one bit per player.
        /// </summary>
        private const int MaxPlayers = 63;

        /// <summary>
        /// Solves the ideal lineup problem using dynamic programming. Calculates the maximum points that can be achieved by selecting players for the given slots, while respecting the position constraints of each slot.
        /// Fairly expensive in terms of time complexity (2^11)
        /// </summary>
        /// <param name="players">The list of players to consider for the lineup. At most 63, so the chosen set fits in a long.</param>
        /// <param name="slots">The list of slots in the roster.</param>
        /// <returns>A tuple containing the maximum points and the list of player IDs in the optimal lineup.</returns>
        /// <exception cref="ArgumentException">More than 63 players were supplied.</exception>
        public static (decimal Points, IReadOnlyList<PlayerId> Lineup) Solve(IReadOnlyList<RosteredPlayer> players, IReadOnlyList<Slot> slots)
        {
            if (players.Count > MaxPlayers)
            {
                throw new ArgumentException(
                    $"The chosen set is tracked as a {MaxPlayers}-bit mask, so at most {MaxPlayers} players are supported; got {players.Count}.",
                    nameof(players));
            }

            var numberOfSlots = slots.Count;
            var dp = new decimal[1 << numberOfSlots];

            // chosen[mask] is the set of player indices making up dp[mask] as a bitmask. 
            var chosen = new long[1 << numberOfSlots];

            Array.Fill(dp, -1m);
            dp[0] = 0m;

            for (var playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                var player = players[playerIndex];

                var next = (decimal[])dp.Clone();
                var nextChosen = (long[])chosen.Clone();

                for (var mask = 0; mask < dp.Length; mask++)
                {
                    if (dp[mask] < 0) continue;
                    for (var s = 0; s < numberOfSlots; s++)
                    {
                        if ((mask & (1 << s)) != 0) continue;
                        if (!slots[s].Accepts(player.FantasyPositions)) continue;

                        var m2 = mask | (1 << s);
                        if (dp[mask] + player.Points > next[m2])
                        {
                            next[m2] = dp[mask] + player.Points;
                            nextChosen[m2] = chosen[mask] | (1L << playerIndex);
                        }
                    }
                }
                dp = next; chosen = nextChosen;
            }

            var best = Array.IndexOf(dp, dp.Max());

            var lineup = new List<PlayerId>();
            for (var playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                if ((chosen[best] & (1L << playerIndex)) != 0)
                {
                    lineup.Add(players[playerIndex].Id);
                }
            }

            return (dp[best], lineup);
        }
    }
}
