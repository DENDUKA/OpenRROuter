using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.ControlPlane.Infrastructure.Startup;
using OpenRussiaRouter.BuildingBlocks.Logging;
using OpenRussiaRouter.BuildingBlocks.Migrations;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("control-plane");

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

await app.ApplySqlMigrationsAsync("ControlPlaneDb", "control", RepositoryMigrationPath.ForService("control"));

Startup.ConfigurePipeline(app);

app.Run();

public partial class Program;
