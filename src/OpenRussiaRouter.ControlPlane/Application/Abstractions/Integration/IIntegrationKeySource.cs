using OpenRussiaRouter.ControlPlane.Application.Contracts;

namespace OpenRussiaRouter.ControlPlane.Application.Abstractions.Integration;

/// <summary>
/// Supplies the authoritative key set to push to nodes.
///
/// It is its own abstraction rather than a direct <c>AuthServiceClient</c> dependency so the
/// sync path can be exercised without a live gRPC server — the alternative in tests is a
/// mocked generated client that returns <c>null</c> from every call and fails somewhere far
/// from the behaviour under test.
/// </summary>
public interface IIntegrationKeySource
{
    /// <summary>
    /// Every non-deleted key with its desired active state.
    /// Returns <c>null</c> when the source is unavailable, so a caller can skip the round
    /// instead of pushing an empty set (which a node would read as "delete everything").
    /// </summary>
    Task<IReadOnlyList<RouterIntegrationKeySyncEntry>?> LoadAsync(CancellationToken cancellationToken);
}
