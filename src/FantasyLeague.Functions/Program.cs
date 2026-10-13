using Azure.Monitor.OpenTelemetry.Exporter;
using FantasyLeague.Data;
using FantasyLeague.Seasons;
using FantasyLeague.Sleeper;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        // UseFunctionsWorkerDefaults only instruments HTTP; without this, a SQL
        // connection attempt - including every retry EnableRetryOnFailure makes
        // during a serverless auto-resume - is invisible in Application Insights
        // except as the warning FinalizedWeekStore logs when it gives up. No
        // SQL text or parameters are captured by default - nothing to opt out of.
        .WithTracing(tracing => tracing.AddSqlClientInstrumentation())
        .UseAzureMonitorExporter();
}

// each library registers only what it owns, so the order of these does not matter
builder.Services.AddSleeperClient();
builder.Services.AddSeasonService();
builder.Services.AddFantasyData(builder.Configuration.GetConnectionString("FantasyLeague"));
builder.Services.Configure<ServiceProviderOptions>(o =>
{
    o.ValidateOnBuild = true;
    o.ValidateScopes = true;
});
builder.Build().Run();

