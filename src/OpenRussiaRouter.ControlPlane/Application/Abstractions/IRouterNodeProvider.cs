using OpenRussiaRouter.ControlPlane.Application.Contracts;
using OpenRussiaRouter.ControlPlane.Domain.Entities;

namespace OpenRussiaRouter.ControlPlane.Application.Abstractions;

public interface IRouterNodeProvider
{
    Task<RouterProviderQuotaResponse> GetProviderQuotaAsync(
        RouterNode node,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<RouterProviderStatusResponse> UpdateProviderStatusAsync(
        RouterNode node,
        string connectionId,
        bool isActive,
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

    Task<RouterIntegrationKeyDeleteResponse> DeleteIntegrationKeyAsync(
        RouterNode node,
        string keyId,
        CancellationToken cancellationToken);

    Task<RouterIntegrationKeysSyncResponse> SyncIntegrationKeysAsync(
        RouterNode node,
        IReadOnlyList<RouterIntegrationKeySyncEntry> keys,
        CancellationToken cancellationToken);
}
