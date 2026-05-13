using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace OpenRussiaRouter.BuildingBlocks.Logging;

/// <summary>
/// Configures structured application logging.
/// </summary>
public static class SeqLoggingExtensions
{
    private const string LoggerName = "OpenRussiaRouter.UnhandledException";

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

    /// <summary>
    /// Logs unhandled HTTP pipeline exceptions before the configured exception handler writes a response.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="errorPath">The exception handler path.</param>
    /// <returns>The same web application.</returns>
    public static WebApplication UseLoggedExceptionHandler(this WebApplication app, string errorPath)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorPath);

        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandlingPath = new PathString(errorPath),
            CreateScopeForErrors = true,
            AllowStatusCode404Response = true
        });

        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception exception)
            {
                LogUnhandledException(context, exception);
                throw;
            }

            var exceptionHandlerFeature = context.Features.Get<IExceptionHandlerFeature>();
            if (exceptionHandlerFeature?.Error is { } handledException)
            {
                LogUnhandledException(context, handledException);
            }
        });

        return app;
    }

    private static void LogUnhandledException(HttpContext context, Exception exception)
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LoggerName);
        logger.LogError(
            exception,
            "Unhandled exception. TraceIdentifier: {TraceIdentifier}; Path: {Path}; Method: {Method}",
            context.TraceIdentifier,
            context.Request.Path.Value,
            context.Request.Method);
    }
}
