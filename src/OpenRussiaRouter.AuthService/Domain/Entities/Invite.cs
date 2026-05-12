using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

public sealed class Invite
{

    public Invite(
        string id,
        string email,
        UserRole role,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        string createdByAdminId,
        DateTimeOffset createdAtUtc)
    {
        Id = Preconditions.RequireValue(id, nameof(id));
        Email = EmailValidator.Normalize(email);
        Role = role;
        TokenHash = Preconditions.RequireValue(tokenHash, nameof(tokenHash));
        ExpiresAtUtc = expiresAtUtc;
        CreatedByAdminId = Preconditions.RequireValue(createdByAdminId, nameof(createdByAdminId));
        CreatedAtUtc = createdAtUtc;
    }

    public string Id { get; }

    public string Email { get; }

    public UserRole Role { get; }

    public string TokenHash { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public string CreatedByAdminId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public bool IsUsed => UsedAtUtc.HasValue;

    public void MarkUsed(DateTimeOffset usedAtUtc)
    {
        if (IsUsed)
        {
            throw new InvalidOperationException("Invite has already been used.");
        }

        UsedAtUtc = usedAtUtc;
    }

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;
}

