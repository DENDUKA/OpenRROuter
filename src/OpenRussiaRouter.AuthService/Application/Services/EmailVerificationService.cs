using System.Security.Cryptography;
using OpenRussiaRouter.AuthService.Domain.Entities;
using OpenRussiaRouter.AuthService.Infrastructure.Security;

namespace OpenRussiaRouter.AuthService.Application.Services;

/// <summary>
/// Issues and checks the 6-digit codes used to confirm a self-serve signup's
/// email address. The raw code is returned to the caller (so the WebApp can mail
/// it) but only its SHA-256 hash is ever persisted.
/// </summary>
public sealed class EmailVerificationService(Sha256SecretHasher secretHasher)
{
    public static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public const int MaxAttempts = 5;

    public IssuedVerification Create(string userId, DateTimeOffset nowUtc)
    {
        var code = GenerateCode();
        var verification = new EmailVerification(
            userId,
            secretHasher.Hash(code),
            nowUtc.Add(CodeTtl),
            nowUtc);
        return new IssuedVerification(verification, code);
    }

    /// <summary>
    /// Re-issues a fresh code on an existing pending verification. Returns the new
    /// raw code, or <c>null</c> if the resend cooldown has not yet elapsed.
    /// </summary>
    public string? Reissue(EmailVerification verification, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (verification.SentAtUtc.Add(ResendCooldown) > nowUtc)
        {
            return null;
        }

        var code = GenerateCode();
        verification.Reissue(secretHasher.Hash(code), nowUtc.Add(CodeTtl), nowUtc);
        return code;
    }

    /// <summary>
    /// Validates a submitted code against a pending verification, mutating the
    /// attempt counter on a wrong code. The caller persists the mutation and, on
    /// <see cref="VerificationCheck.Confirmed"/>, activates the user.
    /// </summary>
    public VerificationCheck Check(EmailVerification verification, string code, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(verification);

        if (verification.IsExpired(nowUtc))
        {
            return VerificationCheck.Expired;
        }

        if (verification.Attempts >= MaxAttempts)
        {
            return VerificationCheck.TooManyAttempts;
        }

        if (string.IsNullOrWhiteSpace(code) || !secretHasher.Verify(code, verification.CodeHash))
        {
            verification.RegisterFailedAttempt();
            return verification.Attempts >= MaxAttempts
                ? VerificationCheck.TooManyAttempts
                : VerificationCheck.WrongCode;
        }

        return VerificationCheck.Confirmed;
    }

    private static string GenerateCode() =>
        RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString("D6");
}

public sealed record IssuedVerification(EmailVerification Verification, string RawCode);

public enum VerificationCheck
{
    Confirmed,
    WrongCode,
    Expired,
    TooManyAttempts
}
