using OpenRussiaRouter.Contracts.Auth;

namespace OpenRussiaRouter.AuthService.Application;

/// <summary>
/// Validates raw API keys for Gateway requests.
/// </summary>
public interface IAuthApiKeyValidator
{
    /// <summary>
    /// Validates a raw API key.
    /// </summary>
    /// <param name="rawKey">Raw API key from the Authorization header.</param>
    /// <returns>Validation result.</returns>
    ValidateApiKeyResult Validate(string rawKey);
}

