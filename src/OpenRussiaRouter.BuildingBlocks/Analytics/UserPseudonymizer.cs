using System.Security.Cryptography;
using System.Text;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Computes a stable, opaque <c>distinct_id</c> for a user via keyed HMAC-SHA256:
/// <c>distinct_id = Base64Url(HMAC-SHA256(IdentitySalt, UserId))</c>. The result is
/// stable for a given (salt, userId) pair, does not contain the raw <c>UserId</c>, and
/// is safe to send to a third-party analytics sink.
/// </summary>
/// <remarks>
/// This component is the single sanctioned HMAC primitive for analytics. It does not
/// handle password hashing, API key generation or session token derivation — those live
/// elsewhere under different primitives.
/// </remarks>
public sealed class UserPseudonymizer
{
    private readonly byte[] _salt;

    public UserPseudonymizer(ProductAnalyticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.IdentitySalt))
        {
            throw new InvalidOperationException(
                $"{ProductAnalyticsOptions.SectionName}:IdentitySalt must be configured when product analytics is in use.");
        }

        _salt = Encoding.UTF8.GetBytes(options.IdentitySalt);
    }

    /// <summary>
    /// Returns the pseudonymized identifier for <paramref name="userId"/>. Two calls with
    /// the same (salt, userId) return the same value; different <c>userId</c> or salt
    /// produce different values.
    /// </summary>
    public string ComputeDistinctId(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        using var hmac = new HMACSHA256(_salt);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(userId));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        // Standard Base64 → strip padding → swap URL-unsafe characters.
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
