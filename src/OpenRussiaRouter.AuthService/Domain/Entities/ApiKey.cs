using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

public sealed class ApiKey
{

    public ApiKey(string id, string userId, string name, string prefix, string secretHash, DateTimeOffset createdAtUtc, string? rawKey = null)
    {
        Id = Preconditions.RequireValue(id, nameof(id));
        UserId = Preconditions.RequireValue(userId, nameof(userId));
        Name = Preconditions.RequireValue(name, nameof(name));
        Prefix = Preconditions.RequireValue(prefix, nameof(prefix));
        SecretHash = Preconditions.RequireValue(secretHash, nameof(secretHash));
        CreatedAtUtc = createdAtUtc;
        RawKey = rawKey;
    }

    public string Id { get; }

    public string UserId { get; }

    public string Name { get; private set; }

    public string Prefix { get; }

    public string SecretHash { get; }

    public string? RawKey { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    public DateTimeOffset? DisabledAtUtc { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public bool IsActive => !DisabledAtUtc.HasValue;

    public bool IsDeleted => DeletedAtUtc.HasValue;

    public void Rename(string name) => Name = Preconditions.RequireValue(name, nameof(name));

    public void Disable(DateTimeOffset disabledAtUtc)
    {
        DisabledAtUtc ??= disabledAtUtc;
    }

    public void Enable()
    {
        DisabledAtUtc = null;
    }

    /// <summary>
    /// Soft-deletes the key so it is hidden from the user's list. Only a disabled
    /// key may be deleted; an active key must be disabled first.
    /// </summary>
    public void Delete(DateTimeOffset deletedAtUtc)
    {
        if (!DisabledAtUtc.HasValue)
        {
            throw new InvalidOperationException("Active API key cannot be deleted; disable it first.");
        }

        DeletedAtUtc ??= deletedAtUtc;
    }

    public void MarkUsed(DateTimeOffset usedAtUtc)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Disabled API key cannot be used.");
        }

        LastUsedAtUtc = usedAtUtc;
    }
}

