using System.Security.Cryptography;
using System.Text;

namespace OpenRussiaRouter.AuthService.Security;

/// <summary>
/// Hashes high-entropy tokens and API keys with SHA-256.
/// </summary>
public sealed class Sha256SecretHasher
{
    /// <summary>
    /// Hashes a raw secret.
    /// </summary>
    /// <param name="rawSecret">Raw secret value.</param>
    /// <returns>Base64 encoded SHA-256 hash.</returns>
    public string Hash(string rawSecret)
    {
        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            throw new ArgumentException("Secret cannot be empty.", nameof(rawSecret));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawSecret));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Verifies a raw secret against a stored hash.
    /// </summary>
    /// <param name="rawSecret">Raw secret value.</param>
    /// <param name="hash">Stored hash.</param>
    /// <returns>Whether the secret matches.</returns>
    public bool Verify(string rawSecret, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        var computed = Hash(rawSecret);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(computed),
            Convert.FromBase64String(hash));
    }
}

