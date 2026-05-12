using Microsoft.AspNetCore.Builder;
using OpenRussiaRouter.BillingService.Infrastructure.Startup;

var builder = WebApplication.CreateBuilder(args);

Startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

Startup.ConfigurePipeline(app);

app.Run();

/// <summary>
/// Billing Service entry point exposed for integration tests.
/// </summary>
public partial class Program;