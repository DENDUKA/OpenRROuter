using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.BillingService.Infrastructure.Startup;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.BuildingBlocks.Migrations;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("billing-service");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.BillingService", "Npgsql");

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

await app.ApplySqlMigrationsAsync("BillingDb", "billing", RepositoryMigrationPath.ForService("billing"));

Startup.ConfigurePipeline(app);
app.MapOpenTelemetryEndpoints();

app.Run();

public partial class Program;
