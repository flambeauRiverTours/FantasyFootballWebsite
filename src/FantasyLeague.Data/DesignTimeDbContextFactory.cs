using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FantasyLeague.Data
{
    /// <summary>
    /// Builds a context for the <c>dotnet ef</c> tools only.
    /// </summary>
    /// <remarks>
    /// <code>
    /// dotnet ef migrations add Name --project src/FantasyLeague.Data --startup-project src/FantasyLeague.Data
    /// </code>
    /// </remarks>
    internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FantasyDataContext>
    {
        /// <summary>The connection string, when one is needed.</summary>
        private const string ConnectionStringVariable = "ConnectionStrings__FantasyLeague";

        /// <summary>A string that parses and deliberately resolves to nothing.</summary>
        private const string ScaffoldOnlyConnectionString =
            "Server=design-time-only;Database=FantasyLeague;Trusted_Connection=True;";

        /// <summary>
        /// Creates the context the tools operate on.
        /// </summary>
        /// <param name="args">Arguments from the tool. Unused.</param>
        /// <returns>A context bound to the SQL Server provider.</returns>
        public FantasyDataContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

            var options = new DbContextOptionsBuilder<FantasyDataContext>()
                .UseSqlServer(string.IsNullOrWhiteSpace(connectionString)
                    ? ScaffoldOnlyConnectionString
                    : connectionString)
                .Options;

            return new FantasyDataContext(options);
        }
    }
}
