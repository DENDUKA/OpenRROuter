using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Integration;
using OpenRussiaRouter.ControlPlane.Application.Contracts;
using OpenRussiaRouter.ControlPlane.Application.Options;
using OpenRussiaRouter.ControlPlane.Domain;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Http;

public sealed class RouterNodeHealthProvider(
    HttpClient httpClient,
    IOptionsMonitor<RouterHealthCheckOptions> options,
    ILogger<RouterNodeHealthProvider>? logger = null)
    : IRouterNodeHealthProvider
{

    public async Task<RouterNodeHealthResult> CheckAsync(RouterNode node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.CurrentValue.Timeout);

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildHealthUri(node.EndpointUrl, options.CurrentValue.HealthPath));
            if (!string.IsNullOrWhiteSpace(node.ControlToken))
            {
                request.Headers.TryAddWithoutValidation(options.CurrentValue.NodeTokenHeader, node.ControlToken);
            }

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token).ConfigureAwait(false);

            var latencyMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (!response.IsSuccessStatusCode)
            {
                return new RouterNodeHealthResult(
                    RouterNodeStatus.Unhealthy,
                    latencyMs,
                    $"http_{(int)response.StatusCode}");
            }

            var status = await TryReadStatusAsync(response, node, timeoutCts.Token).ConfigureAwait(false)
                ?? RouterNodeStatus.Healthy;
            return new RouterNodeHealthResult(status, latencyMs, null);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "Router node health check timed out. NodeId: {NodeId}; EndpointUrl: {EndpointUrl}", node.Id, node.EndpointUrl);
            return new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "timeout");
        }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning(ex, "Router node health check failed. NodeId: {NodeId}; EndpointUrl: {EndpointUrl}", node.Id, node.EndpointUrl);
            return new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "network_error");
        }
    }

    private static Uri BuildHealthUri(Uri endpointUrl, string healthPath)
    {
        var path = string.IsNullOrWhiteSpace(healthPath) ? "/healthz" : healthPath.Trim();
        return new Uri(endpointUrl, path.StartsWith('/') ? path : "/" + path);
    }

    private async Task<RouterNodeStatus?> TryReadStatusAsync(
        HttpResponseMessage response,
        RouterNode node,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("status", out var statusElement))
            {
                return null;
            }

            return statusElement.GetString()?.Trim().ToLowerInvariant() switch
            {
                "healthy" => RouterNodeStatus.Healthy,
                "degraded" => RouterNodeStatus.Degraded,
                "unhealthy" => RouterNodeStatus.Unhealthy,
                _ => null
            };
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Router node health response parsing failed. NodeId: {NodeId}; EndpointUrl: {EndpointUrl}", node.Id, node.EndpointUrl);
            return null;
        }
    }
}
