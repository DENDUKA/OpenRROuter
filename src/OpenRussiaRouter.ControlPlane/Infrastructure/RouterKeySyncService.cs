using Microsoft.Extensions.Logging;
using OpenRussiaRouter.Contracts.Grpc.Auth.V1;
using OpenRussiaRouter.ControlPlane.Application.Services;
using AuthGrpc = OpenRussiaRouter.Contracts.Grpc.Auth.V1.AuthService;

namespace OpenRussiaRouter.ControlPlane.Infrastructure;

public sealed class RouterKeySyncService(
    AuthGrpc.AuthServiceClient authClient,
    IControlPlaneRepository repository,
    RouterNodeKeySyncClient nodeSyncClient,
    ILogger<RouterKeySyncService>? logger = null) : IRouterKeySyncService
{
    public async Task SyncAllAsync(CancellationToken ct)
    {
        var keys = await FetchActiveKeyHashesAsync(ct).ConfigureAwait(false);
        if (keys is null) return;

        var (nodes, _) = repository.ListNodes(limit: 1000);
        var enabled = nodes.Where(n => n.Enabled).ToArray();

        await Task.WhenAll(enabled.Select(node =>
            nodeSyncClient.SyncKeysAsync(node.Id, node.EndpointUrl, node.TokenSecretName, keys, ct)))
            .ConfigureAwait(false);

        logger?.LogInformation("Key sync completed. Nodes: {NodeCount}; Keys: {KeyCount}", enabled.Length, keys.Count);
    }

    public async Task SyncToNodeAsync(string nodeId, CancellationToken ct)
    {
        var node = repository.GetNode(nodeId);
        if (node is null || !node.Enabled)
        {
            logger?.LogWarning("SyncToNodeAsync: node not found or disabled. NodeId: {NodeId}", nodeId);
            return;
        }

        var keys = await FetchActiveKeyHashesAsync(ct).ConfigureAwait(false);
        if (keys is null) return;

        await nodeSyncClient.SyncKeysAsync(node.Id, node.EndpointUrl, node.TokenSecretName, keys, ct)
            .ConfigureAwait(false);
    }

    public async Task RevokeKeyAsync(string prefix, CancellationToken ct)
    {
        var (nodes, _) = repository.ListNodes(limit: 1000);
        var enabled = nodes.Where(n => n.Enabled).ToArray();

        await Task.WhenAll(enabled.Select(node =>
            nodeSyncClient.RevokeKeyAsync(node.Id, node.EndpointUrl, node.TokenSecretName, prefix, ct)))
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyCollection<ApiKeyHashEntry>?> FetchActiveKeyHashesAsync(CancellationToken ct)
    {
        try
        {
            var response = await authClient.ListActiveApiKeyHashesAsync(
                new ListActiveApiKeyHashesRequest(), cancellationToken: ct).ConfigureAwait(false);
            return response.Keys;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to fetch active API key hashes from Auth Service.");
            return null;
        }
    }
}
