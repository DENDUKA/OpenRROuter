namespace OpenRussiaRouter.ApiGateway.Auth;

public sealed record ApiKeyAuthenticationResult(
    bool Succeeded,
    string? UserId,
    string? ApiKeyId,
    string? ApiKeyPrefix,
    string? ErrorCode)
{

    public static ApiKeyAuthenticationResult Success(string userId, string apiKeyId, string apiKeyPrefix)
    {
        return new ApiKeyAuthenticationResult(true, userId, apiKeyId, apiKeyPrefix, null);
    }

    public static ApiKeyAuthenticationResult Failed(string errorCode)
    {
        return new ApiKeyAuthenticationResult(false, null, null, null, errorCode);
    }
}
