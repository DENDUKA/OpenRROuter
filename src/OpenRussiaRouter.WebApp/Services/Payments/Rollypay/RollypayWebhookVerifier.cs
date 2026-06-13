using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenRussiaRouter.WebApp.Services.Payments.Rollypay;

/// <summary>
/// Verifies Rollypay webhook signatures. The signature is a hex-encoded
/// <c>HMAC-SHA256(signing_secret, "{X-Timestamp}.{raw_body}")</c> in the <c>X-Signature</c> header.
/// </summary>
/// <remarks>
/// <para><b>Verify on the RAW body.</b> Any JSON round-trip normalises whitespace / key order and
/// breaks the HMAC — the caller (<see cref="RollypayPaymentProvider.Verify"/>) runs this before
/// any parsing.</para>
/// <para>The golden test for <see cref="ComputeHexHmac"/> is the single most valuable test in the
/// integration: if it is green, real webhooks verify.</para>
/// </remarks>
public static class RollypayWebhookVerifier
{
    /// <summary>Max allowed difference between <c>X-Timestamp</c> and now, to bound replay windows.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static RollypayVerificationResult Verify(
        string signingSecret,
        string rawBody,
        string? timestampHeader,
        string? signatureHeader,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(timestampHeader) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return RollypayVerificationResult.MissingHeaders;
        }

        if (!long.TryParse(timestampHeader, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return RollypayVerificationResult.BadTimestamp;
        }

        var ts = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if ((now - ts).Duration() > MaxClockSkew)
        {
            return RollypayVerificationResult.ExpiredTimestamp;
        }

        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            return RollypayVerificationResult.SecretNotConfigured;
        }

        if (!TryParseHex(signatureHeader.Trim(), out var providedBytes))
        {
            return RollypayVerificationResult.BadSignature;
        }

        var expected = ComputeHexHmac(signingSecret, $"{timestampHeader}.{rawBody}");
        var expectedBytes = HexToBytes(expected);

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes)
            ? RollypayVerificationResult.Valid
            : RollypayVerificationResult.BadSignature;
    }

    public static string ComputeHexHmac(string secret, string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryParseHex(string hex, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            return false;
        }
        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }
        return Convert.FromHexString(hex);
    }
}

public enum RollypayVerificationResult
{
    Valid,
    MissingHeaders,
    BadTimestamp,
    ExpiredTimestamp,
    BadSignature,
    SecretNotConfigured,
}
