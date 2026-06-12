using OpenRussiaRouter.AuthService.Domain.Enums;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

public sealed class UserAccountEntity
{
    public string Id { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public UserStatus Status { get; set; }

    public string? DisplayName { get; set; }

    public int MaxApiKeys { get; set; } = 10;

    public string DisplayCurrency { get; set; } = "RUB";

    public string? PasswordHash { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockedUntilUtc { get; set; }
}