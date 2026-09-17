using FantasyLeague.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace FantasyLeague.Data
{
    /// <summary>
    /// DBContext for the saved-off fantasy computations
    /// </summary>
    public partial class FantasyDataContext : DbContext
    {
        public FantasyDataContext(DbContextOptions<FantasyDataContext> options) : base(options) { }

        //DbSets of all data in the DB
        public DbSet<League> Leagues { get; set; } = null!;
        public DbSet<Player> Players { get; set; } = null!;
        public DbSet<WeekScore> WeekScores { get; set; } = null!;
        public DbSet<Team> Teams { get; set; } = null!;

        /// <summary>
        /// Creates the schema
        /// </summary>
        /// <param name="modelBuilder">Modelbuilder object</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<League>(leagueEntity =>
            {
                leagueEntity.ToTable("Leagues");

                leagueEntity.HasKey(e => e.LeagueId);
                leagueEntity.HasIndex(e => e.Season).IsUnique();
                leagueEntity.Property(e => e.Name).IsRequired();
                leagueEntity.Property(e => e.RosterPositionsJson).HasColumnType("nvarchar(max)").IsRequired();
                leagueEntity.Property(e => e.UpdatedUtc).IsRequired();

                leagueEntity.Property(e => e.PreviousLeagueId); //not required - null for first year of league

            });

            modelBuilder.Entity<Player>(player =>
            {
                player.ToTable("Players");

                player.HasKey(e => e.PlayerId);
                player.Property(e => e.FullName).IsRequired();
                player.Property(e => e.PositionsJson).IsRequired();
                player.Property(e => e.UpdatedUtc).IsRequired();

                player.Property(e => e.Team); //not required - non-rostered players
            });

            modelBuilder.Entity<Team>(team =>
            {
                team.ToTable("Teams");

                team.HasKey(e => new { e.LeagueId, e.RosterId });
                team.Property(e => e.LeagueId).IsRequired();
                team.Property(e => e.RosterId).IsRequired();
                team.Property(e => e.TeamName).IsRequired();
                team.Property(e => e.OwnerDisplayName).IsRequired();
                team.Property(e => e.OwnerUserId).IsRequired();
            });

            modelBuilder.Entity<WeekScore>(weekScore =>
            {
                weekScore.ToTable("WeekScores");

                weekScore.HasKey(e => new { e.Season, e.LeagueId, e.RosterId, e.Week });
                weekScore.Property(e => e.Season).IsRequired();
                weekScore.Property(e => e.LeagueId).IsRequired();
                weekScore.Property(e => e.RosterId).IsRequired();
                weekScore.Property(e => e.Week).IsRequired();
                weekScore.Property(e => e.StartersJson).HasColumnType("nvarchar(max)").IsRequired();
                weekScore.Property(e => e.PlayersPointsJson).HasColumnType("nvarchar(max)").IsRequired();
                weekScore.Property(e => e.ActualPts).HasPrecision(9, 2).IsRequired();
                weekScore.Property(e => e.IdealPts).HasPrecision(9, 2).IsRequired();
                weekScore.Property(e => e.IdealLineupJson).HasColumnType("nvarchar(max)").IsRequired();
                weekScore.Property(e => e.SlotAveragesJson).HasColumnType("nvarchar(max)").IsRequired();
                weekScore.Property(e => e.IsFinal).IsRequired();
                weekScore.Property(e => e.ComputedUtc).IsRequired();
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
