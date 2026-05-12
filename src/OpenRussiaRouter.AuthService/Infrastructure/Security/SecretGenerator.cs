using System.Security.Cryptography;

namespace OpenRussiaRouter.AuthService.Infrastructure.Security;

/// <summary>
/// Generates URL-safe high-entropy secrets.
/// </summary>
public sealed class SecretGenerator : ISecretGenerator
{
    /// <inheritdoc />
    public string Generate(string prefix, int bytes = 32)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("Prefix cannot be empty.", nameof(prefix));
        }

        if (bytes < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "Secret must contain at least 16 random bytes.");
        }

        var random = RandomNumberGenerator.GetBytes(bytes);
        var encoded = Convert.ToBase64String(random)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return $"{prefix}_{encoded}";
    }
}
