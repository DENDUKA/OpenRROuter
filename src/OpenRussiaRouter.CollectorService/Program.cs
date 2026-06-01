using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.CollectorService.Infrastructure.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("collector");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.Collector", "OpenRussiaRouter");

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

// Eagerly resolve ProviderQuotaMeter so its observable gauge registers with the meter infrastructure.
_ = app.Services.GetRequiredService<OpenRussiaRouter.CollectorService.Application.Quota.ProviderQuotaMeter>();

Startup.ConfigurePipeline(app);
app.MapOpenTelemetryEndpoints();

app.Run();

public partial class Program;
