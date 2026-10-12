using FantasyLeague.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace FantasyLeague.Data
{
    /// <summary>
    /// The SQL record of every finalized week (A20).
    /// </summary>
    /// <param name="options">Provider and connection, from the host or the design-time factory.</param>
    internal sealed class FantasyDataContext(DbContextOptions<FantasyDataContext> options) : DbContext(options)
    {
        /// <summary>
        /// Gets one row per roster per week.
        /// </summary>
        public DbSet<WeekScore> WeekScores => Set<WeekScore>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<WeekScore>(weekScore =>
            {
                weekScore.ToTable("WeekScores");

                // leads on Season: every read is one season's (A14)
                weekScore.HasKey(e => new { e.Season, e.LeagueId, e.RosterId, e.Week });
                weekScore.Property(e => e.ActualPts).HasPrecision(9, 2);
                weekScore.Property(e => e.IdealPts).HasPrecision(9, 2);
            });
    }
}
