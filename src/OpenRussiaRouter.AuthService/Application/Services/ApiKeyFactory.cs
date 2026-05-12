using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

/// <summary>
/// Creates API keys and validates raw key material.
/// </summary>
public sealed class ApiKeyFactory(Sha256SecretHasher secretHasher)
{
    private const string ApiKeyPrefix = "orr";
    private const int VisiblePrefixLength = 12;

    /// <summary>
    /// Creates a new API key.
    /// </summary>
    /// <param name="userId">Owner user id.</param>
    /// <param name="name">User-visible key name.</param>
    /// <param name="createdAtUtc">Creation timestamp.</param>
    /// <returns>API key plus raw secret shown once.</returns>
    public CreatedApiKey Create(string userId, string name, DateTimeOffset createdAtUtc)
    {
        var rawKey = SecretGenerator.Generate(ApiKeyPrefix, 36);
        var prefix = rawKey[..Math.Min(rawKey.Length, VisiblePrefixLength)];
        var apiKey = new ApiKey(
            Guid.NewGuid().ToString("N"),
            userId,
            name,
            prefix,
            secretHasher.Hash(rawKey),
            createdAtUtc);

        return new CreatedApiKey(apiKey, rawKey);
    }

    /// <summary>
    /// Verifies a raw API key against stored key metadata.
    /// </summary>
    /// <param name="rawKey">Raw API key.</param>
    /// <param name="apiKey">Stored API key.</param>
    /// <param name="usedAtUtc">Successful use timestamp.</param>
    /// <returns>Whether the key is active and matches.</returns>
    public bool VerifyAndMarkUsed(string rawKey, ApiKey apiKey, DateTimeOffset usedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(apiKey);

        if (!apiKey.IsActive || !secretHasher.Verify(rawKey, apiKey.SecretHash))
        {
            return false;
        }

        apiKey.MarkUsed(usedAtUtc);
        return true;
    }
}

/// <summary>
/// API key creation result containing stored metadata and raw key shown once.
/// </summary>
/// <param name="ApiKey">Stored API key.</param>
/// <param name="RawKey">Raw API key.</param>
public sealed record CreatedApiKey(ApiKey ApiKey, string RawKey);

