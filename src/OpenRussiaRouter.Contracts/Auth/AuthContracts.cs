namespace OpenRussiaRouter.Contracts.Auth;

/// <summary>
/// Result of API key validation.
/// </summary>
/// <param name="IsValid">Whether the API key can be used.</param>
/// <param name="UserId">Validated user id.</param>
/// <param name="ApiKeyId">Validated API key id.</param>
/// <param name="ErrorCode">Machine-readable error code for invalid keys.</param>
public sealed record ValidateApiKeyResult(
    bool IsValid,
    string? UserId,
    string? ApiKeyId,
    string? ErrorCode);

/// <summary>
/// API key metadata shown in the user cabinet.
/// </summary>
/// <param name="Id">API key id.</param>
/// <param name="Name">User-visible key name.</param>
/// <param name="Prefix">Safe key prefix.</param>
/// <param name="CreatedAtUtc">Creation timestamp.</param>
/// <param name="LastUsedAtUtc">Last successful use timestamp.</param>
/// <param name="RevokedAtUtc">Revocation timestamp.</param>
public sealed record ApiKeySummary(
    string Id,
    string Name,
    string Prefix,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? RevokedAtUtc);

