using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.BillingService.Infrastructure.Auth;
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

// T-003: refuse to start outside Development without real s2s tokens — the shipped
// appsettings.json keeps both tokens blank, so a missing ServiceAuth__Tokens__<caller> env
// would otherwise leave the money-printing RPCs (T-002) without enforceable auth.
ServiceAuthStartupGuard.Validate(app.Services, app.Environment);

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
