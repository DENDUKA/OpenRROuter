namespace OpenRussiaRouter.ApiGateway.Auth;

public interface IApiKeyProvider
{

    Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken);
}

