using OpenRussiaRouter.AuthService.Application.Abstractions;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

public sealed class ApiKeyFactory(ISecretHasher secretHasher, ISecretGenerator secretGenerator)
{
    private const string ApiKeyPrefix = "sk-or";
    private const int VisiblePrefixLength = 12;

    public CreatedApiKey Create(string userId, string name, DateTimeOffset createdAtUtc)
    {
        var rawKey = secretGenerator.Generate(ApiKeyPrefix, 36, separator: '-');
        var prefix = rawKey[..Math.Min(rawKey.Length, VisiblePrefixLength)];
        var apiKey = new ApiKey(
            Guid.NewGuid().ToString("N"),
            userId,
            name,
            prefix,
            secretHasher.Hash(rawKey),
            createdAtUtc,
            rawKey);

        return new CreatedApiKey(apiKey, rawKey);
    }

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

public sealed record CreatedApiKey(ApiKey ApiKey, string RawKey);

public sealed record CreateApiKeyResult(bool Succeeded, CreatedApiKey? Created, string? ErrorCode)
{
    public static CreateApiKeyResult Success(CreatedApiKey created) => new(true, created, null);
    public static CreateApiKeyResult Failed(string errorCode) => new(false, null, errorCode);
}

