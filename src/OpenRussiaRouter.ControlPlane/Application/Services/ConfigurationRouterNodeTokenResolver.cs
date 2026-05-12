using Microsoft.Extensions.Options;
using OpenRussiaRouter.ControlPlane.Application.Services;

namespace OpenRussiaRouter.ControlPlane.Application.Services;

public sealed class ConfigurationRouterNodeTokenResolver(IOptionsMonitor<RouterHealthCheckOptions> options)
    : IRouterNodeTokenResolver
{

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
