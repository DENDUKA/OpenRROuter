using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRussiaRouter.ControlPlane.Application;
using OpenRussiaRouter.ControlPlane.Infrastructure.Persistence;
using OpenRussiaRouter.BuildingBlocks.Health;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services
    .AddOptions<RouterHealthCheckOptions>()
    .Bind(builder.Configuration.GetSection("RouterHealthChecks"))
    .ValidateDataAnnotations();
builder.Services
    .AddOptions<RouterLeaseCleanupOptions>()
    .Bind(builder.Configuration.GetSection("RouterLeaseCleanup"))
    .ValidateDataAnnotations();
var controlDbConnectionString = builder.Configuration.GetConnectionString("ControlPlaneDb");
if (!string.IsNullOrWhiteSpace(controlDbConnectionString))
{
    builder.Services.AddSingleton<IControlPlaneDbConnectionFactory>(_ => new NpgsqlControlPlaneDbConnectionFactory(controlDbConnectionString));
    builder.Services.AddSingleton<IControlPlaneStore, DapperControlPlaneStore>();
    builder.Services.AddSingleton<RouterControlPlaneService>();
}
else
{
    builder.Services.AddSingleton<IControlPlaneStore, InMemoryControlPlaneStore>();
    builder.Services.AddSingleton<RouterControlPlaneService>();
}

builder.Services.AddSingleton<IRouterNodeTokenResolver, ConfigurationRouterNodeTokenResolver>();
builder.Services.AddHttpClient<IRouterNodeHealthClient, HttpRouterNodeHealthClient>();
builder.Services.AddHostedService<RouterHealthCheckWorker>();
builder.Services.AddHostedService<RouterLeaseCleanupWorker>();

var app = builder.Build();

app.MapGrpcService<GrpcRouterControlPlaneService>();
app.MapGet("/healthz", () => ServiceHealth.Ok("control-plane"));
app.MapGet("/ready", () => ServiceHealth.Ok("control-plane"));

app.MapGet("/", () => Results.Json(new
{
    service = "OpenRussiaRouter Router Control Plane",
    contracts = new[] { "SelectRouterNode", "ReleaseRouterNode", "ListRouterNodes" }
}));

app.Run();
