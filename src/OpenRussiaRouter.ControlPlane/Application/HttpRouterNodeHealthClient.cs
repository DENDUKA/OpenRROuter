using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenRussiaRouter.Contracts.ControlPlane;
using OpenRussiaRouter.ControlPlane.Domain;

namespace OpenRussiaRouter.ControlPlane.Application;

/// <summary>
/// HTTP health client for external 9Router nodes.
/// </summary>
public sealed class HttpRouterNodeHealthClient(
    HttpClient httpClient,
    IRouterNodeTokenResolver tokenResolver,
    IOptionsMonitor<RouterHealthCheckOptions> options)
    : IRouterNodeHealthClient
{
    /// <inheritdoc />
    public async Task<RouterNodeHealthResult> CheckAsync(RouterNode node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);

        var token = tokenResolver.Resolve(node.TokenSecretName);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "node_token_missing");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.CurrentValue.Timeout);

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildHealthUri(node.EndpointUrl, options.CurrentValue.HealthPath));
            request.Headers.TryAddWithoutValidation(options.CurrentValue.NodeTokenHeader, token);

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

            var status = await TryReadStatusAsync(response, timeoutCts.Token).ConfigureAwait(false)
                ?? RouterNodeStatus.Healthy;
            return new RouterNodeHealthResult(status, latencyMs, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "timeout");
        }
        catch (HttpRequestException)
        {
            return new RouterNodeHealthResult(RouterNodeStatus.Unhealthy, null, "network_error");
        }
    }

    private static Uri BuildHealthUri(Uri endpointUrl, string healthPath)
    {
        var path = string.IsNullOrWhiteSpace(healthPath) ? "/healthz" : healthPath.Trim();
        return new Uri(endpointUrl, path.StartsWith('/') ? path : "/" + path);
    }

    private static async Task<RouterNodeStatus?> TryReadStatusAsync(
        HttpResponseMessage response,
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
        catch (JsonException)
        {
            return null;
        }
    }
}
