using System.Security.Cryptography;
using System.Text;

namespace OpenRussiaRouter.BuildingBlocks.Analytics;

/// <summary>
/// Computes the opaque site-visitor identifier:
/// <c>visitor_hash = Base64Url(HMAC-SHA256(IdentitySalt, ip + "|" + userAgent))</c>.
/// </summary>
/// <remarks>
/// The raw IP never leaves this class — only the hash is stored, which is why no cookie
/// and no consent banner are involved. The salt is stable (not rotated daily) on purpose:
/// without a stable value there is no way to tell a returning visitor from a new one, and
/// the visit → signup funnel would be impossible. Known limits, spelled out on the
/// dashboard panels: people behind one NAT collapse into a single visitor, and
/// <c>X-Forwarded-For</c> is client-controlled, so the count can be inflated deliberately.
/// </remarks>
public sealed class VisitorIdHasher
{
    private readonly byte[] _salt;

    public VisitorIdHasher(ProductAnalyticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.IdentitySalt))
        {
            throw new InvalidOperationException(
                $"{ProductAnalyticsOptions.SectionName}:IdentitySalt must be configured when site visit tracking is in use.");
        }

        _salt = Encoding.UTF8.GetBytes(options.IdentitySalt);
    }

    /// <summary>
    /// Returns the visitor hash for the given client IP and user agent. Both parts are
    /// included so that two devices sharing an address are told apart when their user
    /// agents differ. Missing values are normalized to <c>unknown</c> rather than
    /// throwing: an unidentifiable visitor still has to be counted.
    /// </summary>
    public string ComputeVisitorHash(string? clientIp, string? userAgent)
    {
        var ip = string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp.Trim();
        var agent = string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent.Trim();

        using var hmac = new HMACSHA256(_salt);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ip}|{agent}"));

        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
