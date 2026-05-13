using Microsoft.AspNetCore.Builder;
using Serilog;

namespace OpenRussiaRouter.BuildingBlocks.Logging;

/// <summary>
/// Configures structured application logging.
/// </summary>
public static class SeqLoggingExtensions
{
    /// <summary>
    /// Replaces default logging with Serilog console logging and optional Seq ingestion.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="defaultServiceName">The service name used when configuration does not override it.</param>
    /// <returns>The same web application builder.</returns>
    public static WebApplicationBuilder AddSeqLogging(this WebApplicationBuilder builder, string defaultServiceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var serviceName = builder.Configuration["Seq:ServiceName"] ?? defaultServiceName;
        var seqUrl = builder.Configuration["Seq:Url"];

        builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName)
                .WriteTo.Console();

            if (!string.IsNullOrWhiteSpace(seqUrl))
            {
                loggerConfiguration.WriteTo.Seq(seqUrl);
            }
        });

        return builder;
    }
}
