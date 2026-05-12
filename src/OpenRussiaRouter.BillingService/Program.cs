using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.BillingService.Application.Interfaces;
using OpenRussiaRouter.BillingService.Application.Services;
using OpenRussiaRouter.BillingService.Infrastructure.Persistence;
using OpenRussiaRouter.BuildingBlocks.Health;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
var billingDbConnectionString = builder.Configuration.GetConnectionString("BillingDb");
if (!string.IsNullOrWhiteSpace(billingDbConnectionString))
{
    builder.Services.AddSingleton<IBillingDbConnectionFactory>(_ => new NpgsqlBillingDbConnectionFactory(billingDbConnectionString));
    builder.Services.AddSingleton<IBillingRepository, DapperBillingRepository>();
    builder.Services.AddSingleton<BillingApplicationService>();
}
else
{
    builder.Services.AddSingleton<IBillingRepository, InMemoryBillingRepository>();
    builder.Services.AddSingleton<BillingApplicationService>();
}

var app = builder.Build();

app.MapGrpcService<GrpcBillingService>();
app.MapGet("/healthz", () => ServiceHealth.Ok("billing-service"));
app.MapGet("/ready", () => ServiceHealth.Ok("billing-service"));

app.MapGet("/", () => Results.Json(new
{
    service = "OpenRussiaRouter Billing / Usage Service",
    contracts = new[] { "CheckBalanceAndModel", "RecordUsage", "ListPublicModels", "GetBalance", "CreateVirtualTopUp", "UpsertModelPrice", "ListUsage", "ListBillingAnomalies" }
}));

app.Run();

/// <summary>
/// Billing Service entry point exposed for integration tests.
/// </summary>
public partial class Program;