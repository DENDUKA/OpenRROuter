using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Http;

public sealed class CachedRouterNodeProvider(
    RouterNodeProvider inner,
    RouterNodeProviderCacheStore cache,
    IOptionsMonitor<RouterNodeProviderCacheOptions> options)
    : IRouterNodeProvider
{
    public string DescribeListProvidersRequest(RouterNode node) =>
        inner.DescribeListProvidersRequest(node);

    public string DescribeListModelsRequest(RouterNode node) =>
        inner.DescribeListModelsRequest(node);

    public Task<RouterProviderQuotaResponse> GetProviderQuotaAsync(
        RouterNode node,
        bool includeInactive,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(
            node,
            $"quota::{includeInactive}",
            ct => inner.GetProviderQuotaAsync(node, includeInactive, ct),
            cancellationToken);

    public async Task<RouterProviderStatusResponse> UpdateProviderStatusAsync(
        RouterNode node,
        string connectionId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var response = await inner.UpdateProviderStatusAsync(node, connectionId, isActive, cancellationToken)
            .ConfigureAwait(false);
        cache.InvalidateNode(node.Id);
        return response;
    }

    public Task<RouterProviderModelsResponse> ListProviderModelsAsync(
        RouterNode node,
        string connectionId,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(
            node,
            $"provider-models::{connectionId.Trim()}",
            ct => inner.ListProviderModelsAsync(node, connectionId, ct),
            cancellationToken);

    public Task<RouterIntegrationKeysResponse> ListIntegrationKeysAsync(
        RouterNode node,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(
            node,
            "integration-keys",
            ct => inner.ListIntegrationKeysAsync(node, ct),
            cancellationToken);

    public async Task<RouterIntegrationKeyCreateResponse> CreateIntegrationKeyAsync(
        RouterNode node,
        string name,
        string key,
        CancellationToken cancellationToken)
    {
        var response = await inner.CreateIntegrationKeyAsync(node, name, key, cancellationToken)
            .ConfigureAwait(false);
        cache.InvalidateNode(node.Id);
        return response;
    }

    public async Task<RouterIntegrationKeyInfo> UpdateIntegrationKeyStatusAsync(
        RouterNode node,
        string keyId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var response = await inner.UpdateIntegrationKeyStatusAsync(node, keyId, isActive, cancellationToken)
            .ConfigureAwait(false);
        cache.InvalidateNode(node.Id);
        return response;
    }

    public Task<NodeModelsResponse> ListModelsAsync(
        RouterNode node,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(
            node,
            "models",
            ct => inner.ListModelsAsync(node, ct),
            cancellationToken);

    public Task<RouterProviderListResponse> ListProvidersAsync(
        RouterNode node,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(
            node,
            "providers",
            ct => inner.ListProvidersAsync(node, ct),
            cancellationToken);

    private async Task<TResponse> GetOrCreateAsync<TResponse>(
        RouterNode node,
        string operationKey,
        Func<CancellationToken, Task<TResponse>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);

        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
            return await factory(cancellationToken).ConfigureAwait(false);

        var key = RouterNodeProviderCacheStore.CreateNodePrefix(node.Id) + operationKey;
        var nowUtc = DateTimeOffset.UtcNow;
        if (cache.TryGet<TResponse>(key, nowUtc, out var cached))
            return cached;

        var response = await factory(cancellationToken).ConfigureAwait(false);
        cache.Set(key, response, nowUtc.Add(currentOptions.Ttl));
        return response;
    }
}
