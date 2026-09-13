using FantasyLeague.Console;
using FantasyLeague.Sleeper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// the library's own logs would interleave with the table
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSleeperClient();

using var host = builder.Build();

var leagueId = Environment.GetEnvironmentVariable("SLEEPER_LEAGUE_ID");
if (string.IsNullOrWhiteSpace(leagueId))
{
    Console.Write("League ID: ");
    leagueId = Console.ReadLine();
}

// never parsed: Sleeper ids are numeric-looking strings too long for an int
if (string.IsNullOrWhiteSpace(leagueId))
{
    Console.Error.WriteLine("No league ID supplied.");
    return 1;
}

await host.Services.GetRequiredService<IPlayerMapRefresher>().EnsureCurrentAsync(default);
var snapshot = await host.Services.GetRequiredService<ISeasonService>().BuildAsync(leagueId.Trim(), default);

StandingsRenderer.Render(snapshot, System.Console.Out, DateTimeOffset.UtcNow);

return snapshot.Violations.Count > 0 || snapshot.UnresolvedPlayerIds.Count > 0 ? 1 : 0;
