namespace OpenRussiaRouter.ApiGateway.Auth;

public sealed record ApiKeyAuthenticationResult(
    bool Succeeded,
    string? UserId,
    string? ApiKeyId,
    string? ApiKeyPrefix,
    string? ErrorCode,
    bool IsAdmin = false)
{

    public static ApiKeyAuthenticationResult Success(string userId, string apiKeyId, string apiKeyPrefix, bool isAdmin = false)
    {
        return new ApiKeyAuthenticationResult(true, userId, apiKeyId, apiKeyPrefix, null, isAdmin);
    }

    public static ApiKeyAuthenticationResult Failed(string errorCode)
    {
        return new ApiKeyAuthenticationResult(false, null, null, null, errorCode);
    }
}
