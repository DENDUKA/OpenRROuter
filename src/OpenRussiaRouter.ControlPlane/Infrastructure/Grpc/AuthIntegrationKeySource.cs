using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenRussiaRouter.ControlPlane.Application.Abstractions.Integration;
using OpenRussiaRouter.ControlPlane.Application.Contracts;
using AuthContracts = OpenRussiaRouter.Contracts.Grpc.Auth.V1;

namespace OpenRussiaRouter.ControlPlane.Infrastructure.Grpc;

/// <summary>
/// The real key source: AuthService's "all non-deleted raw keys" RPC.
/// </summary>
public sealed class AuthIntegrationKeySource(
    AuthContracts.AuthService.AuthServiceClient authClient,
    ILogger<AuthIntegrationKeySource> logger)
    : IIntegrationKeySource
{
    public async Task<IReadOnlyList<RouterIntegrationKeySyncEntry>?> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await authClient.ListAllNonDeletedApiKeysRawAsync(
                new AuthContracts.ListAllNonDeletedApiKeysRawRequest(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return response.Keys
                .Select(k => new RouterIntegrationKeySyncEntry
                {
                    Name = k.Name,
                    Key = k.RawKey,
                    IsActive = k.IsActive
                })
                .ToList();
        }
        catch (Exception ex) when (ex is RpcException or HttpRequestException or InvalidOperationException)
        {
            // Null, not an empty list: an empty push would look like "every key was deleted"
            // and strip the whole fleet.
            logger.LogWarning(ex, "Failed to load API keys from AuthService; skipping integration key sync");
            return null;
        }
    }
}
