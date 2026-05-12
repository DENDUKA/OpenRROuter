namespace OpenRussiaRouter.Contracts.Auth;

public sealed record ValidateApiKeyResult(
    bool IsValid,
    string? UserId,
    string? ApiKeyId,
    string? ErrorCode);

public sealed record ApiKeySummary(
    string Id,
    string Name,
    string Prefix,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? RevokedAtUtc);

