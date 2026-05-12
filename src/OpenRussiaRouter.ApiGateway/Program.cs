using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.ApiGateway.Infrastructure.Startup;

var builder = WebApplication.CreateBuilder(args);

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

Startup.ConfigurePipeline(app);

app.Run();

/// <summary>
/// API Gateway entry point exposed for integration tests.
/// </summary>
public partial class Program;