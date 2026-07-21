using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.BuildingBlocks.Migrations;
using OpenRussiaRouter.ContentService.Infrastructure.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("content");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.ContentService", "Npgsql", "OpenRussiaRouter");

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

await app.ApplySqlMigrationsAsync("ContentDb", "content", RepositoryMigrationPath.ForService("content"));

Startup.ConfigurePipeline(app);
app.MapOpenTelemetryEndpoints();

app.Run();

public partial class Program;
