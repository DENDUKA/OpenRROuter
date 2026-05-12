using OpenRussiaRouter.AuthService.Domain.Enums;
using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

public sealed class UserAccount
{

    public UserAccount(string id, string email, UserRole role, string passwordHash, DateTimeOffset createdAtUtc)
    {
        Id = Preconditions.RequireValue(id, nameof(id));
        Email = EmailValidator.Normalize(email);
        Role = role;
        PasswordHash = Preconditions.RequireValue(passwordHash, nameof(passwordHash));
        CreatedAtUtc = createdAtUtc;
    }

    public string Id { get; }

    public string Email { get; }

    public UserRole Role { get; }

    public UserStatus Status { get; private set; } = UserStatus.Active;

    public string PasswordHash { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public void Disable() => Status = UserStatus.Disabled;

}

