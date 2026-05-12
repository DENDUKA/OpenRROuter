using Microsoft.AspNetCore.Http;

namespace OpenRussiaRouter.ApiGateway.Auth;

public static class BearerTokenReader
{
    private const string Prefix = "Bearer ";

    public static bool TryRead(HttpRequest request, out string token)
    {
        token = string.Empty;

        if (!request.Headers.TryGetValue("Authorization", out var values))
        {
            return false;
        }

        return TryRead(values.ToString(), out token);
    }

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

