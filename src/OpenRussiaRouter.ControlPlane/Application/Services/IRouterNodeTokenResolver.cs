namespace OpenRussiaRouter.ControlPlane.Application.Services;

public interface IRouterNodeTokenResolver
{

    string? Resolve(string tokenSecretName);
}
