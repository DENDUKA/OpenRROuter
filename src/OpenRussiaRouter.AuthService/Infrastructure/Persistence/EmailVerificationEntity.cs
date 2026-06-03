namespace OpenRussiaRouter.AuthService.Infrastructure.Persistence;

public sealed class EmailVerificationEntity
{
    public string UserId { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset SentAtUtc { get; set; }

    public int Attempts { get; set; }
}
