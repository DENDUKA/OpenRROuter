using System.Security.Cryptography;
using System.Text;
using OpenRussiaRouter.AuthService.Application.Abstractions;

namespace OpenRussiaRouter.AuthService.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 hasher for low-entropy secrets (6-digit verification codes).
/// Uses a server key so precomputed rainbow tables are useless.
/// Deterministic: same (secret, serverKey) always produces the same hash,
/// enabling DB lookups by hash.
/// </summary>
public sealed class HmacSecretHasher : ISecretHasher
{
    private readonly byte[] _serverKey;

    /// <summary>
    /// Creates an <see cref="HmacSecretHasher"/> with the default development key.
    /// Production must use the constructor with a real key from configuration.
    /// </summary>
    public static HmacSecretHasher CreateDev() =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("rwt-dev-code-hmac-key-do-not-use-in-production")));

    public HmacSecretHasher(byte[] serverKey)
    {
        if (serverKey.Length == 0)
        {
            throw new ArgumentException("Server key cannot be empty.", nameof(serverKey));
        }

        _serverKey = serverKey;
    }

    public string Hash(string rawSecret)
    {
        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            throw new ArgumentException("Secret cannot be empty.", nameof(rawSecret));
        }

        var hash = HMACSHA256.HashData(_serverKey, Encoding.UTF8.GetBytes(rawSecret));
        return Convert.ToBase64String(hash);
    }

    public bool Verify(string rawSecret, string hash)
    {
        if (string.IsNullOrWhiteSpace(rawSecret) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        byte[] hashBytes;
        try
        {
            hashBytes = Convert.FromBase64String(hash);
        }
        catch (FormatException)
        {
            return false;
        }

        var computed = Hash(rawSecret);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(computed),
            hashBytes);
    }
}
