using System.ComponentModel.DataAnnotations;

namespace FantasyLeague.Seasons
{
    /// <summary>
    /// How long the slim player map stays good for, bound from the
    /// <c>PlayerMap</c> configuration section.
    /// </summary>
    internal sealed class PlayerMapOptions
    {
        /// <summary>
        /// Gets or sets how old a map may be before it is refetched from Sleeper.
        /// </summary>
        [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
        public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(24);
    }
}
