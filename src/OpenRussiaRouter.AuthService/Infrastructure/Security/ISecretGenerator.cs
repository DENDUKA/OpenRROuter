namespace OpenRussiaRouter.AuthService.Infrastructure.Security;

/// <summary>
/// Generates URL-safe high-entropy secrets.
/// </summary>
public interface ISecretGenerator
{
    /// <summary>
    /// Generates a URL-safe secret.
    /// </summary>
    /// <param name="prefix">Human-readable product prefix.</param>
    /// <param name="bytes">Random byte count before encoding.</param>
    /// <returns>Generated secret.</returns>
    string Generate(string prefix, int bytes = 32);
}
