using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public interface IRouterNodeProvider
{
    string DescribeListProvidersRequest(RouterNode node);

    string DescribeListModelsRequest(RouterNode node);

    Task<RouterProviderQuotaResponse> GetProviderQuotaAsync(
        RouterNode node,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<RouterProviderStatusResponse> UpdateProviderStatusAsync(
        RouterNode node,
        string connectionId,
        bool isActive,
        CancellationToken cancellationToken);

    Task<RouterProviderModelsResponse> ListProviderModelsAsync(
        RouterNode node,
        string connectionId,
        CancellationToken cancellationToken);

    Task<RouterIntegrationKeysResponse> ListIntegrationKeysAsync(
        RouterNode node,
        CancellationToken cancellationToken);

    Task<RouterIntegrationKeyCreateResponse> CreateIntegrationKeyAsync(
        RouterNode node,
        string name,
        string key,
        CancellationToken cancellationToken);

    Task<RouterIntegrationKeyInfo> UpdateIntegrationKeyStatusAsync(
        RouterNode node,
        string keyId,
        bool isActive,
        CancellationToken cancellationToken);

    Task<NodeModelsResponse> ListModelsAsync(
        RouterNode node,
        CancellationToken cancellationToken);

    Task<RouterProviderListResponse> ListProvidersAsync(
        RouterNode node,
        CancellationToken cancellationToken);
}
