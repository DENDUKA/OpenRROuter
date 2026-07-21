using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.BillingService.Infrastructure.Startup;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Metrics;
using OpenRussiaRouter.BuildingBlocks.Migrations;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("billing-service");
// DefaultMeterName обязателен: без него кастомные инструменты (LimitMetrics) молча не экспортируются.
builder.AddOpenTelemetryMetrics("OpenRussiaRouter.BillingService", "Npgsql", OpenTelemetryExtensions.DefaultMeterName);

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

await app.ApplySqlMigrationsAsync("BillingDb", "billing", RepositoryMigrationPath.ForService("billing"));

if (app.Configuration.GetValue("SqlMigrations:Enabled", true))
{
    var productAnalyticsConnectionString = app.Configuration["ProductAnalytics:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(productAnalyticsConnectionString))
    {
        var analyticsMigrations = SqlMigrationLoader.LoadFromDirectory(RepositoryMigrationPath.ForService("analytics"));
        var analyticsRunner = new PostgresSqlMigrationRunner(productAnalyticsConnectionString);
        await analyticsRunner.ApplyAsync("analytics", analyticsMigrations);
    }
}

Startup.ConfigurePipeline(app);
app.MapOpenTelemetryEndpoints();

app.Run();

public partial class Program;
