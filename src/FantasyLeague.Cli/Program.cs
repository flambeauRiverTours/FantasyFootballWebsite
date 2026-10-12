using FantasyLeague.Cli;
using FantasyLeague.Core;
using FantasyLeague.Seasons;
using FantasyLeague.Sleeper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

var builder = Host.CreateApplicationBuilder(args);

// stderr, so the library's own logs never interleave with the table on stdout
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSleeperClient();
builder.Services.AddSeasonService();

// no database or storage account: rebuild every week and refetch the player map each run
builder.Services.AddSingleton<IFinalizedWeekStore, NullFinalizedWeekStore>();
builder.Services.AddSingleton<IPlayerMapStore, NullPlayerMapStore>();

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

var snapshot = await host.Services.GetRequiredService<ISeasonService>().BuildAsync(default);
var payload = SeasonPayloadMapper.ToSeasonPayload(
    snapshot,
    host.Services.GetRequiredService<TimeProvider>().GetUtcNow());

StandingsRenderer.Render(payload, snapshot, Console.Out);

return snapshot.Violations.Count > 0 || snapshot.UnresolvedPlayerIds.Count > 0 ? 1 : 0;
