using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.ApiGateway.Infrastructure.Startup;
using OpenRussiaRouter.BuildingBlocks.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.AddSeqLogging("api-gateway");

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

Startup.ConfigurePipeline(app);

app.Run();

public partial class Program;
