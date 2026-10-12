using FantasyLeague.Console;
using FantasyLeague.Sleeper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// stderr, so the library's own logs never interleave with the table on stdout
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSleeperClient();

// the League section the Functions host binds, e.g. League__CurrentLeagueId
if (string.IsNullOrWhiteSpace(builder.Configuration["League:CurrentLeagueId"]))
{
    Console.Write("League ID: ");
    builder.Configuration["League:CurrentLeagueId"] = Console.ReadLine()?.Trim();
}

if (string.IsNullOrWhiteSpace(builder.Configuration["League:CurrentLeagueId"]))
{
    Console.Error.WriteLine("No league ID supplied.");
    return 1;
}

using var host = builder.Build();

var league = host.Services.GetRequiredService<IOptions<LeagueOptions>>().Value;
var snapshot = await host.Services.GetRequiredService<ISeasonService>().BuildAsync(league.CurrentLeagueId, default);
var excluded = SeasonPayloadMapper.ResolveExcludedRosters(snapshot, league.ExcludedOwnerIds);
var payload = SeasonPayloadMapper.ToIndividualSeasonPayload(
    snapshot,
    host.Services.GetRequiredService<TimeProvider>().GetUtcNow(),
    excluded.RosterIds,
    league.OverrideLeagueName);

StandingsRenderer.Render(payload, snapshot, System.Console.Out);

return snapshot.Violations.Count > 0 || snapshot.UnresolvedPlayerIds.Count > 0 ? 1 : 0;
