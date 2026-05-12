namespace OpenRussiaRouter.ApiGateway.Router.Interfaces;

/// <summary>
/// Resolves node tokens from local Gateway configuration.
/// </summary>
public interface IRouterNodeTokenProvider
{
    /// <summary>
    /// Resolves the token value for a Control Plane secret reference.
    /// </summary>
    /// <param name="tokenSecretName">Secret reference name.</param>
    /// <returns>Raw token.</returns>
    string Resolve(string tokenSecretName);
}

