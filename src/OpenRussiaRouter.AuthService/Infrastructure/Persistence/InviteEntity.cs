using OpenRussiaRouter.AuthService.Domain.Enums;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

public sealed class InviteEntity
{
    public string Id { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public string CreatedByAdminId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UsedAtUtc { get; set; }
}