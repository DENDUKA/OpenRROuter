using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

public sealed class ApiKey
{

    public ApiKey(string id, string userId, string name, string prefix, string secretHash, DateTimeOffset createdAtUtc)
    {
        Id = Preconditions.RequireValue(id, nameof(id));
        UserId = Preconditions.RequireValue(userId, nameof(userId));
        Name = Preconditions.RequireValue(name, nameof(name));
        Prefix = Preconditions.RequireValue(prefix, nameof(prefix));
        SecretHash = Preconditions.RequireValue(secretHash, nameof(secretHash));
        CreatedAtUtc = createdAtUtc;
    }

    public string Id { get; }

    public string UserId { get; }

    public string Name { get; private set; }

    public string Prefix { get; }

    public string SecretHash { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive => !RevokedAtUtc.HasValue;

    public void Rename(string name) => Name = Preconditions.RequireValue(name, nameof(name));

    public void Revoke(DateTimeOffset revokedAtUtc)
    {
        RevokedAtUtc ??= revokedAtUtc;
    }

    public void MarkUsed(DateTimeOffset usedAtUtc)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Revoked API key cannot be used.");
        }

        LastUsedAtUtc = usedAtUtc;
    }
}

