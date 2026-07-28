using OpenRussiaRouter.AuthService.Domain.Enums;

namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

public sealed class ApiKeyEntity
{
    public string Id { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public string SecretHash { get; set; } = string.Empty;

    public string? RawKey { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public DateTimeOffset? DisabledAtUtc { get; set; }

    public DateTimeOffset? DeletedAtUtc { get; set; }

    public UserRole? UserRole { get; set; }
}