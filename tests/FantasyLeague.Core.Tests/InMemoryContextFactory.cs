using FantasyLeague.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FantasyLeague.Core.Tests
{
    /// <summary>
    /// Contexts on an in-memory database of their own, counted, or a failure in
    /// place of a context once <see cref="Failure"/> is set.
    /// </summary>
    /// <param name="name">The database; contexts sharing a name share its rows.</param>
    internal sealed class InMemoryContextFactory(string? name = null) : IDbContextFactory<FantasyDataContext>
    {
        private readonly string database = name ?? Guid.NewGuid().ToString();

        public int Created { get; private set; }

        public Exception? Failure { get; set; }

        public FantasyDataContext CreateDbContext()
        {
            Created++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return new FantasyDataContext(
                new DbContextOptionsBuilder<FantasyDataContext>()
                    .UseInMemoryDatabase(database)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                    .Options);
        }
    }
}
