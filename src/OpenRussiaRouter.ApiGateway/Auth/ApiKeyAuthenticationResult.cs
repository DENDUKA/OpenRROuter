namespace OpenRussiaRouter.ApiGateway.Auth;

public sealed record ApiKeyAuthenticationResult(
    bool Succeeded,
    string? UserId,
    string? ApiKeyId,
    string? ErrorCode)
{

    public static ApiKeyAuthenticationResult Success(string userId, string apiKeyId)
    {
        return new ApiKeyAuthenticationResult(true, userId, apiKeyId, null);
    }

    public static ApiKeyAuthenticationResult Failed(string errorCode)
    {
        return new ApiKeyAuthenticationResult(false, null, null, errorCode);
    }
}

