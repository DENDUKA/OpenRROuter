namespace OpenRussiaRouter.ApiGateway.Auth;

public interface IApiKeyAuthenticator
{

    Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken);
}

