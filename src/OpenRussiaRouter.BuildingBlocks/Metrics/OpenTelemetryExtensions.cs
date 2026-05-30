using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace OpenRussiaRouter.BuildingBlocks.Metrics;

/// <summary>
/// Configures OpenTelemetry metrics for the application.
/// </summary>
public static class OpenTelemetryExtensions
{
    public static WebApplicationBuilder AddOpenTelemetryMetrics(this WebApplicationBuilder builder, string serviceName, params string[] additionalMeters)
    {
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddPrometheusExporter();

                foreach (var meter in additionalMeters)
                {
                    metrics.AddMeter(meter);
                }
            });

        return builder;
    }

    public static WebApplication MapOpenTelemetryEndpoints(this WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint();
        return app;
    }
}
