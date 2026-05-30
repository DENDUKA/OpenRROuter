using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.ApiGateway.Infrastructure.Startup;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("api-gateway");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.ApiGateway");

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

Startup.ConfigurePipeline(app);
app.MapOpenTelemetryEndpoints();

app.Run();

public partial class Program;
