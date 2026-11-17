namespace FantasyLeague.Core
{
    /// <summary>
    /// Where a playoff side comes from before its roster is known (T69).
    /// </summary>
    public enum BracketSource
    {
        /// <summary>Seeded straight into its round, or not yet seeded.</summary>
        None,

        /// <summary>The winner of another match.</summary>
        WinnerOf,

        /// <summary>The loser of another match.</summary>
        LoserOf,
    }
}
