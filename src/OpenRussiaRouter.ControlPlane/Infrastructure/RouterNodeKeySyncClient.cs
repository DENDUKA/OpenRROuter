using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using OpenRussiaRouter.ControlPlane.Application.Services;

namespace OpenRussiaRouter.ControlPlane.Infrastructure;

public sealed class RouterNodeKeySyncClient(
    HttpClient httpClient,
    IRouterNodeTokenResolver tokenResolver,
    ILogger<RouterNodeKeySyncClient>? logger = null)
{
    private const string NodeTokenHeader = "X-ORR-Node-Token";

    public async Task SyncKeysAsync(
        string nodeId,
        Uri nodeEndpoint,
        string tokenSecretName,
        IEnumerable<ApiKeyHashEntry> keys,
        CancellationToken ct)
    {
        var token = tokenResolver.Resolve(tokenSecretName);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger?.LogWarning("Node token missing for sync. NodeId: {NodeId}", nodeId);
            return;
        }

        var payload = new
        {
            keys = keys.Select(k => new { prefix = k.Prefix, secret_hash = k.SecretHash })
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(nodeEndpoint, "/internal/keys/sync"));
        request.Headers.TryAddWithoutValidation(NodeTokenHeader, token);
        request.Content = JsonContent.Create(payload);

        try
        {
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger?.LogWarning("Key sync failed for node. NodeId: {NodeId}; Status: {Status}", nodeId, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException { CancellationToken: { IsCancellationRequested: false } })
        {
            logger?.LogWarning(ex, "Key sync request failed for node. NodeId: {NodeId}; Endpoint: {Endpoint}", nodeId, nodeEndpoint);
        }
    }

    public async Task RevokeKeyAsync(
        string nodeId,
        Uri nodeEndpoint,
        string tokenSecretName,
        string prefix,
        CancellationToken ct)
    {
        var token = tokenResolver.Resolve(tokenSecretName);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger?.LogWarning("Node token missing for key revoke. NodeId: {NodeId}", nodeId);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(nodeEndpoint, $"/internal/keys/{Uri.EscapeDataString(prefix)}"));
        request.Headers.TryAddWithoutValidation(NodeTokenHeader, token);

        try
        {
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && (int)response.StatusCode != 404)
            {
                logger?.LogWarning("Key revoke failed for node. NodeId: {NodeId}; Prefix: {Prefix}; Status: {Status}", nodeId, prefix, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException { CancellationToken: { IsCancellationRequested: false } })
        {
            logger?.LogWarning(ex, "Key revoke request failed for node. NodeId: {NodeId}; Prefix: {Prefix}", nodeId, prefix);
        }
    }
}
