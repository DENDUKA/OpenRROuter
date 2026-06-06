namespace OpenRussiaRouter.Contracts.Auth;

public sealed record ValidateApiKeyResult(
    bool IsValid,
    string? UserId,
    string? ApiKeyId,
    string? ApiKeyPrefix,
    string? ErrorCode,
    string? Role = null);

public sealed record ApiKeySummary(
    string Id,
    string Name,
    string Prefix,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? DisabledAtUtc,
    bool IsDeleted);

