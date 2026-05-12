using Microsoft.Extensions.Configuration;
using OpenRussiaRouter.ApiGateway.Router.Interfaces;

namespace OpenRussiaRouter.ApiGateway.Router;

/// <summary>
/// Resolves router node tokens from configuration section <c>RouterNodeTokens</c>.
/// </summary>
public sealed class ConfigurationRouterNodeTokenProvider(IConfiguration configuration) : IRouterNodeTokenProvider
{
    /// <inheritdoc />
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
