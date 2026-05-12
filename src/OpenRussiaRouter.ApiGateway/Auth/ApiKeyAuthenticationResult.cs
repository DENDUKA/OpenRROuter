namespace OpenRussiaRouter.ApiGateway.Auth;

/// <summary>
/// Result of Gateway API key authentication.
/// </summary>
/// <param name="Succeeded">Whether authentication succeeded.</param>
/// <param name="UserId">Authenticated user id.</param>
/// <param name="ApiKeyId">Authenticated API key id.</param>
/// <param name="ErrorCode">Machine-readable error code.</param>
public sealed record ApiKeyAuthenticationResult(
    bool Succeeded,
    string? UserId,
    string? ApiKeyId,
    string? ErrorCode)
{
    /// <summary>
    /// Creates a successful authentication result.
    /// </summary>
    /// <param name="userId">Authenticated user id.</param>
    /// <param name="apiKeyId">Authenticated API key id.</param>
    /// <returns>Successful result.</returns>
    public static ApiKeyAuthenticationResult Success(string userId, string apiKeyId)
    {
        return new ApiKeyAuthenticationResult(true, userId, apiKeyId, null);
    }

    /// <summary>
    /// Creates a failed authentication result.
    /// </summary>
    /// <param name="errorCode">Machine-readable error code.</param>
    /// <returns>Failed result.</returns>
    public static ApiKeyAuthenticationResult Failed(string errorCode)
    {
        return new ApiKeyAuthenticationResult(false, null, null, errorCode);
    }
}

