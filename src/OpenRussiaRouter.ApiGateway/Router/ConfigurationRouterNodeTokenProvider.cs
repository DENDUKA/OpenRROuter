using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.ApiGateway.Router;

namespace OpenRussiaRouter.ApiGateway.Router;

public sealed class ConfigurationRouterNodeTokenProvider(IConfiguration configuration) : IRouterNodeTokenProvider
{

    public string Resolve(string tokenSecretName)
    {
        if (string.IsNullOrWhiteSpace(tokenSecretName))
        {
            throw new ArgumentException("Token secret name cannot be empty.", nameof(tokenSecretName));
        }

        var token = configuration[$"RouterNodeTokens:{tokenSecretName}"];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"Router node token '{tokenSecretName}' is not configured.");
        }

        return token;
    }
}
