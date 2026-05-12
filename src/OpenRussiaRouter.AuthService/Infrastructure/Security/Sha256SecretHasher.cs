using System.Security.Cryptography;
using System.Text;

namespace OpenRussiaRouter.AuthService.Infrastructure.Security;

public sealed class Sha256SecretHasher
{

    public string Hash(string rawSecret)
    {
        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            throw new ArgumentException("Secret cannot be empty.", nameof(rawSecret));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawSecret));
        return Convert.ToBase64String(bytes);
    }

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

