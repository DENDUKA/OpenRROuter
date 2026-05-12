namespace OpenRussiaRouter.ApiGateway.Auth;

/// <summary>
/// Validates public API keys before Gateway handles a request.
/// </summary>
public interface IApiKeyAuthenticator
{
    /// <summary>
    /// Authenticates a raw API key.
    /// </summary>
    /// <param name="rawApiKey">Raw API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authentication result.</returns>
    Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken);
}

