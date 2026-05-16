using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.ApiGateway.Router;

namespace OpenRussiaRouter.ApiGateway.Router;

public sealed class ConfigurationRouterNodeTokenProvider(IConfiguration configuration) : IRouterNodeTokenProvider
{

    public string Resolve(string tokenSecretName)
    {
        if (string.IsNullOrWhiteSpace(tokenSecretName))
            return string.Empty;

        return configuration[$"RouterNodeTokens:{tokenSecretName}"] ?? string.Empty;
    }
}
