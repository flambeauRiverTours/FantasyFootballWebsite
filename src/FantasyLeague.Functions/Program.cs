using Azure.Monitor.OpenTelemetry.Exporter;
using FantasyLeague.Sleeper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

// Registers the configured league and the client together with its resilience
// pipeline and rate limit; the host does not need to know how it is assembled.
builder.Services.AddSleeperClient();
builder.Services.Configure<ServiceProviderOptions>(o =>
{
    o.ValidateOnBuild = true;
    o.ValidateScopes = true;
});
builder.Build().Run();

