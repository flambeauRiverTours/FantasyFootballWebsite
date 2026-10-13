namespace FantasyLeague.Core
{
    /// <summary>
    /// One startable slot from a league's <c>roster_positions</c>.
    /// </summary>
    /// <param name="Code">Sleeper's slot code, such as <c>QB</c> or <c>SUPER_FLEX</c>.</param>
    public sealed record Slot(string Code)
    {
        /// <summary>
        /// The positions each flex slot accepts. Every other slot accepts only its own code.
        /// </summary>
        private static readonly Dictionary<string, string[]> Flex = new()
        {
            ["FLEX"] = ["RB", "WR", "TE"],
            ["SUPER_FLEX"] = ["QB", "RB", "WR", "TE"],
            ["REC_FLEX"] = ["WR", "TE"],
            ["WRRB_FLEX"] = ["RB", "WR"],
            ["IDP_FLEX"] = ["DL", "LB", "DB"],
        };

        /// <summary>
        /// Sleeper's underscored flex codes, spaced out for the client.
        /// </summary>
        private static readonly Dictionary<string, string> DisplayNames = new()
        {
            ["SUPER_FLEX"] = "SUPER FLEX",
            ["REC_FLEX"] = "REC FLEX",
            ["WRRB_FLEX"] = "WR/RB FLEX",
            ["IDP_FLEX"] = "IDP FLEX",
        };

        /// <summary>
        /// The label to show the client for a slot code. Every non-flex code is
        /// already client-ready and passes through unchanged.
        /// </summary>
        /// <param name="code">A raw Sleeper slot code.</param>
        /// <returns>The code's display form.</returns>
        public static string DisplayCode(string code) => DisplayNames.GetValueOrDefault(code, code);

        /// <summary>
        /// Slots that take exactly one position.
        /// </summary>
        private static readonly HashSet<string> PositionSlots =
            ["QB", "RB", "WR", "TE", "K", "DEF", "DL", "LB", "DB"];

        /// <summary>
        /// Roster places that are not part of a lineup.
        /// </summary>
        private static readonly HashSet<string> NotStartable = ["BN", "IR", "TAXI"];

        /// <summary>
        /// Reads a league's <c>roster_positions</c> into its startable slots, in order.
        /// </summary>
        /// <param name="rosterPositions">The league's roster positions, bench included.</param>
        /// <returns>Every startable slot, duplicates included.</returns>
        /// <exception cref="ArgumentException">A code is not recognised (A3).</exception>
        /// <remarks>
        /// An unknown code fails loudly. Dropped silently, it would undercount
        /// ideal, and <c>ideal &gt;= actual</c> cannot see undercounting.
        /// </remarks>
        public static IReadOnlyList<Slot> Parse(IEnumerable<string> rosterPositions)
        {
            var all = rosterPositions.ToList();
            var unknown = all.Where(c => !NotStartable.Contains(c)
                              && !Flex.ContainsKey(c)
                              && !PositionSlots.Contains(c))
                     .Distinct()
                     .ToList();
            if (unknown.Count > 0)
            {
                throw new ArgumentException($"Unrecognised roster slot code(s): {string.Join(", ", unknown)}. " +
                        $"Full roster_positions: [{string.Join(", ", all)}]", nameof(rosterPositions));
            }

            return all.Where(c => !NotStartable.Contains(c)).Select(c => new Slot(c)).ToList();
        }

        /// <summary>
        /// Decides whether a player may start in this slot.
        /// </summary>
        /// <param name="positions">The player's fantasy positions, all of them, not just the primary one.</param>
        /// <returns><c>true</c> when any of them fits.</returns>
        public bool Accepts(IReadOnlyList<string> positions) =>
            Flex.TryGetValue(Code, out var allowed)
                ? positions.Any(allowed.Contains)
                : positions.Contains(Code);
    }
}
