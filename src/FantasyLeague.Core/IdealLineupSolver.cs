namespace FantasyLeague.Core
{
    /// <summary>
    /// Finds the most points a roster could have started (D4).
    /// </summary>
    /// <remarks>
    /// An exact dynamic programme over which slots are filled, not a greedy
    /// pick. Greedy is wrong whenever a flex slot exists, and it undercounts,
    /// which is the one direction an ideal score must never be wrong in.
    /// </remarks>
    public static class IdealLineupSolver
    {
        /// <summary>
        /// The most players a single call can consider. The chosen set is carried
        /// as a <see cref="long"/> bitmask, one bit per player.
        /// </summary>
        private const int MaxPlayers = 63;

        /// <summary>
        /// Assigns players to slots to maximise total points, each player used at most once.
        /// </summary>
        /// <param name="players">Everyone rostered that week. At most 63, so the chosen set fits in a long.</param>
        /// <param name="slots">The startable slots.</param>
        /// <returns>The best total and the players who make it up.</returns>
        /// <remarks>
        /// One state per subset of filled slots, so the work is players × 2^slots ×
        /// slots: about 450,000 steps for 20 players and 11 slots.
        /// </remarks>
        /// <exception cref="ArgumentException">More than 63 players were supplied.</exception>
        public static (decimal IdealPts, IReadOnlyList<PlayerId> Lineup) Solve(IReadOnlyList<RosteredPlayer> players, IReadOnlyList<Slot> slots)
        {
            if (players.Count > MaxPlayers)
            {
                throw new ArgumentException(
                    $"The chosen set is tracked as a {MaxPlayers}-bit mask, so at most {MaxPlayers} players are supported; got {players.Count}.",
                    nameof(players));
            }

            var numberOfSlots = slots.Count;
            var dp = new decimal[1 << numberOfSlots];

            // chosen[mask] is the set of player indices making up dp[mask], as a bitmask
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
                    if (dp[mask] < 0)
                    {
                        continue;
                    }

                    for (var s = 0; s < numberOfSlots; s++)
                    {
                        if ((mask & (1 << s)) != 0)
                        {
                            continue;
                        }

                        if (!slots[s].Accepts(player.FantasyPositions))
                        {
                            continue;
                        }

                        var filled = mask | (1 << s);
                        if (dp[mask] + player.Points > next[filled])
                        {
                            next[filled] = dp[mask] + player.Points;
                            nextChosen[filled] = chosen[mask] | (1L << playerIndex);
                        }
                    }
                }

                dp = next;
                chosen = nextChosen;
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
