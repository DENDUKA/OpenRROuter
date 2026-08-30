using Microsoft.AspNetCore.Http;

namespace OpenRussiaRouter.ApiGateway.Auth;

public enum ApiKeyReadStatus
{
    None,
    Success,
    Conflict
}

public static class ApiKeyReader
{
    private const string BearerPrefix = "Bearer ";

    public static ApiKeyReadStatus TryRead(HttpRequest request, out string token)
    {
        token = string.Empty;

        var hasBearer = false;
        var bearerToken = string.Empty;
        if (request.Headers.TryGetValue("Authorization", out var authValues))
        {
            if (BearerTokenReader.TryRead(authValues.ToString(), out var parsedBearer))
            {
                hasBearer = true;
                bearerToken = parsedBearer;
            }
        }

        var hasApiKey = false;
        var apiKeyValue = string.Empty;
        if (request.Headers.TryGetValue("x-api-key", out var apiKeyValues))
        {
            var raw = apiKeyValues.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                hasApiKey = true;
                apiKeyValue = raw;
            }
        }

        if (hasBearer && hasApiKey)
        {
            if (!string.Equals(bearerToken, apiKeyValue, StringComparison.Ordinal))
            {
                return ApiKeyReadStatus.Conflict;
            }

            token = bearerToken;
            return ApiKeyReadStatus.Success;
        }

        if (hasBearer)
        {
            token = bearerToken;
            return ApiKeyReadStatus.Success;
        }

        if (hasApiKey)
        {
            token = apiKeyValue;
            return ApiKeyReadStatus.Success;
        }

        return ApiKeyReadStatus.None;
    }
}
