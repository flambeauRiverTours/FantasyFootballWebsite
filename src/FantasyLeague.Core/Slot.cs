namespace FantasyLeague.Core
{
    /// <summary>
    /// Defines a roster slot that can take multiple positions, such as "FLEX" or "SUPER_FLEX". It also includes methods to parse roster positions and determine if a slot can accept certain positions.
    /// </summary>
    /// <param name="Code">The position code for the slot.</param>
    public sealed record Slot(string Code)
    {
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
        /// A set of all valid position slots in the fantasy league, including quarterback (QB), running back (RB), wide receiver (WR), tight end (TE), kicker (K), defense (DEF), defensive line (DL), linebacker (LB), and defensive back (DB).
        /// </summary>
        private static readonly HashSet<string> PositionSlots =
            ["QB", "RB", "WR", "TE", "K", "DEF", "DL", "LB", "DB"];

        private static readonly HashSet<string> NotStartable = ["BN", "IR", "TAXI"];

        /// <summary>
        /// Parses a list of roster positions into a list of startable slots, filtering out non-startable positions like "BN", "IR", and "TAXI".
        /// </summary>
        /// <param name="rosterPositions">The list of roster positions to parse.</param>
        /// <returns>The list of startable slots.</returns>
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
        /// Determines whether the slot can accept any of the given positions, taking into account flex positions.
        /// </summary>
        /// <param name="positions">The list of positions to check.</param>
        /// <returns>true if the slot can accept any of the positions; otherwise, false.</returns>
        public bool Accepts(IReadOnlyList<string> positions) =>
            Flex.TryGetValue(Code, out var allowed)
                ? positions.Any(allowed.Contains)
                : positions.Contains(Code);
    }
}
