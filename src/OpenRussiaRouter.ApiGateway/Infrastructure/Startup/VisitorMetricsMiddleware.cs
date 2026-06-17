using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using OpenRussiaRouter.BuildingBlocks.Metrics;

namespace OpenRussiaRouter.ApiGateway.Infrastructure.Startup;

public sealed class VisitorMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Meter Meter = new(OpenTelemetryExtensions.DefaultMeterName);

    private static readonly Counter<long> RequestCounter =
        Meter.CreateCounter<long>(
            "orr_http_requests_total",
            description: "Total HTTP requests with client IP");

    private static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>(
            "orr_http_request_duration_seconds",
            unit: "s",
            description: "HTTP request duration in seconds");

    private static readonly Histogram<long> ResponseSize =
        Meter.CreateHistogram<long>(
            "orr_http_response_size_bytes",
            unit: "By",
            description: "HTTP response body size in bytes");

    public VisitorMetricsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = NormalizePath(context.Request.Path.Value ?? "/");
        var method = context.Request.Method;
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var sw = Stopwatch.StartNew();
        Exception? error = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            var status = context.Response.StatusCode.ToString();
            var duration = sw.Elapsed.TotalSeconds;

            var tags = new KeyValuePair<string, object?>[]
            {
                new("client_ip", clientIp),
                new("method", method),
                new("path", path),
                new("status", status)
            };

            RequestCounter.Add(1, tags);

            var durationTags = new KeyValuePair<string, object?>[]
            {
                new("method", method),
                new("path", path),
                new("status", status)
            };
            RequestDuration.Record(duration, durationTags);

            if (context.Response.ContentLength is > 0)
            {
                ResponseSize.Record(context.Response.ContentLength.Value, durationTags);
            }
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return "/";

        if (path.StartsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
            return "/v1/chat/completions";

        if (path.StartsWith("/v1/models", StringComparison.OrdinalIgnoreCase))
            return "/v1/models";

        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            return "/health";

        if (path.StartsWith("/ready", StringComparison.OrdinalIgnoreCase))
            return "/ready";

        if (path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase))
            return "/metrics";

        return path;
    }
}
