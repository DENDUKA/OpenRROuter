using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Resolves router node tokens from strongly typed configuration.
/// </summary>
public sealed class ConfigurationRouterNodeTokenResolver(IOptionsMonitor<RouterHealthCheckOptions> options)
    : IRouterNodeTokenResolver
{
    /// <inheritdoc />
    public string? Resolve(string tokenSecretName)
    {
        if (string.IsNullOrWhiteSpace(tokenSecretName))
        {
            return null;
        }

        return options.CurrentValue.NodeTokens.TryGetValue(tokenSecretName.Trim(), out var token)
            && !string.IsNullOrWhiteSpace(token)
                ? token
                : null;
    }
}
