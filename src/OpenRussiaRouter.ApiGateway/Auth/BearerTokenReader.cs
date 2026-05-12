using Microsoft.AspNetCore.Http;

namespace OpenRussiaRouter.ApiGateway.Auth;

/// <summary>
/// Reads Bearer API keys from public HTTP requests.
/// </summary>
public static class BearerTokenReader
{
    private const string Prefix = "Bearer ";

    /// <summary>
    /// Attempts to read a Bearer token from the Authorization header.
    /// </summary>
    /// <param name="request">HTTP request.</param>
    /// <param name="token">Extracted token.</param>
    /// <returns>Whether a non-empty Bearer token was present.</returns>
    public static bool TryRead(HttpRequest request, out string token)
    {
        token = string.Empty;

        if (!request.Headers.TryGetValue("Authorization", out var values))
        {
            return false;
        }

        return TryRead(values.ToString(), out token);
    }

    /// <summary>
    /// Attempts to read a Bearer token from a raw Authorization header value.
    /// </summary>
    /// <param name="authorizationHeader">Raw Authorization header.</param>
    /// <param name="token">Extracted token.</param>
    /// <returns>Whether a non-empty Bearer token was present.</returns>
    public static bool TryRead(string? authorizationHeader, out string token)
    {
        token = string.Empty;

        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = authorizationHeader[Prefix.Length..].Trim();
        return token.Length > 0;
    }
}

