using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.CollectorService.Infrastructure.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("collector");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.Collector", OpenTelemetryExtensions.DefaultMeterName);

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

Startup.ConfigurePipeline(app);
app.MapOpenTelemetryEndpoints();

app.Run();


public partial class Program;
