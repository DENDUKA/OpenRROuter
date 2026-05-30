using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.AuthService.Infrastructure.Startup;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.BuildingBlocks.Migrations;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("auth-service");
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.AuthService", "Npgsql");

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

await app.ApplySqlMigrationsAsync("AuthDb", "auth", RepositoryMigrationPath.ForService("auth"));

Startup.ConfigurePipeline(app);
app.MapOpenTelemetryEndpoints();

app.Run();

public partial class Program;
