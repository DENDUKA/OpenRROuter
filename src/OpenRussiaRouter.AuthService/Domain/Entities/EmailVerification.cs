using OpenRussiaRouter.BuildingBlocks.Common;

namespace OpenRussiaRouter.AuthService.Domain.Entities;

/// <summary>
/// A pending email-confirmation challenge for a self-serve signup. One row per
/// not-yet-verified user; holds the SHA-256 hash of the 6-digit code (never the
/// code itself), its expiry, when it was last sent (for resend cooldown), and the
/// number of failed confirmation attempts.
/// </summary>
public sealed class EmailVerification
{
    public EmailVerification(
        string userId,
        string codeHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset sentAtUtc,
        int attempts = 0)
    {
        UserId = Preconditions.RequireValue(userId, nameof(userId));
        CodeHash = Preconditions.RequireValue(codeHash, nameof(codeHash));
        ExpiresAtUtc = expiresAtUtc;
        SentAtUtc = sentAtUtc;
        Attempts = attempts;
    }

    public string UserId { get; }

    public string CodeHash { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset SentAtUtc { get; private set; }

    public int Attempts { get; private set; }

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    public void RegisterFailedAttempt() => Attempts++;

    /// <summary>Replaces the code (resend), resetting expiry, sent-time and the attempt counter.</summary>
    public void Reissue(string codeHash, DateTimeOffset expiresAtUtc, DateTimeOffset sentAtUtc)
    {
        CodeHash = Preconditions.RequireValue(codeHash, nameof(codeHash));
        ExpiresAtUtc = expiresAtUtc;
        SentAtUtc = sentAtUtc;
        Attempts = 0;
    }
}
