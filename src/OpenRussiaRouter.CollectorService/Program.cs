using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.CollectorService.Application.Quota;
using OpenRussiaRouter.CollectorService.Infrastructure.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("collector");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.Collector", OpenTelemetryExtensions.DefaultMeterName);

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

_ = app.Services.GetRequiredService<ProviderQuotaMeter>();

Startup.ConfigurePipeline(app);
app.MapOpenTelemetryEndpoints();

app.Run();


public partial class Program;
