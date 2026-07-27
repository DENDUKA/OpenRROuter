using System.Security.Cryptography;
using System.Text;
using OpenRussiaRouter.AuthService.Application.Abstractions;

namespace OpenRussiaRouter.AuthService.Infrastructure.Security;

public sealed class Sha256SecretHasher : ISecretHasher
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

