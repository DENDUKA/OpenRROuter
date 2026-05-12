namespace OpenRussiaRouter.AuthService.Domain.Entities;

/// <summary>
/// Stored API key metadata and secret hash.
/// </summary>
public sealed class ApiKey
{
    /// <summary>
    /// Creates API key metadata.
    /// </summary>
    /// <param name="id">API key id.</param>
    /// <param name="userId">Owner user id.</param>
    /// <param name="name">User-visible name.</param>
    /// <param name="prefix">Safe visible key prefix.</param>
    /// <param name="secretHash">Hash of the raw key.</param>
    /// <param name="createdAtUtc">Creation timestamp.</param>
    public ApiKey(string id, string userId, string name, string prefix, string secretHash, DateTimeOffset createdAtUtc)
    {
        Id = RequireValue(id, nameof(id));
        UserId = RequireValue(userId, nameof(userId));
        Name = RequireValue(name, nameof(name));
        Prefix = RequireValue(prefix, nameof(prefix));
        SecretHash = RequireValue(secretHash, nameof(secretHash));
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// API key id.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Owner user id.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// User-visible name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Safe key prefix.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// Hash of raw API key.
    /// </summary>
    public string SecretHash { get; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Last successful validation timestamp.
    /// </summary>
    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    /// <summary>
    /// Revocation timestamp.
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <summary>
    /// True when the key can be used.
    /// </summary>
    public bool IsActive => !RevokedAtUtc.HasValue;

    /// <summary>
    /// Renames the API key.
    /// </summary>
    /// <param name="name">New display name.</param>
    public void Rename(string name) => Name = RequireValue(name, nameof(name));

    /// <summary>
    /// Marks the key as revoked.
    /// </summary>
    /// <param name="revokedAtUtc">Revocation timestamp.</param>
    public void Revoke(DateTimeOffset revokedAtUtc)
    {
        RevokedAtUtc ??= revokedAtUtc;
    }

    /// <summary>
    /// Marks successful key use.
    /// </summary>
    /// <param name="usedAtUtc">Use timestamp.</param>
    public void MarkUsed(DateTimeOffset usedAtUtc)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Revoked API key cannot be used.");
        }

        LastUsedAtUtc = usedAtUtc;
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", name);
        }

        return value.Trim();
    }
}

