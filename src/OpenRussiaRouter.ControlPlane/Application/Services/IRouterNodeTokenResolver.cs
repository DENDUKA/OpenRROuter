namespace OpenRussiaRouter.ControlPlane.Application.Services;

/// <summary>
/// Resolves a configured router node token from a secret reference.
/// </summary>
public interface IRouterNodeTokenResolver
{
    /// <summary>
    /// Resolves the raw token for the provided secret name.
    /// </summary>
    /// <param name="tokenSecretName">Token secret reference stored on the node.</param>
    /// <returns>Raw token or null when the secret is not configured.</returns>
    string? Resolve(string tokenSecretName);
}
