using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.ApiGateway.Infrastructure.Startup;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.BuildingBlocks.Migrations;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("api-gateway");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.ApiGateway", OpenTelemetryExtensions.DefaultMeterName);

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

await app.ApplySqlMigrationsAsync("GatewayDb", "gateway", RepositoryMigrationPath.ForService("gateway"));

Startup.ConfigurePipeline(app);
app.MapOpenTelemetryEndpoints();

app.Run();

public partial class Program;
